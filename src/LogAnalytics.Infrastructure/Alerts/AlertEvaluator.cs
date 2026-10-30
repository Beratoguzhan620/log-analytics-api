using LogAnalytics.Core.Alerts;
using LogAnalytics.Infrastructure.Persistence;
using LogAnalytics.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogAnalytics.Infrastructure.Alerts;

/// <summary>Checks every rule on a timer. The firing is saved first and sent afterwards, so a slow webhook never loses it.</summary>
public sealed partial class AlertEvaluation(
    LogsDbContext db, LogQueries queries, IAlertNotifier notifier, TimeProvider clock, ILogger<AlertEvaluation> logger)
{
    private const long AdvisoryLock = 0x41_4C_52_54;

    public async Task<IReadOnlyList<AlertFiring>> RunAsync(CancellationToken cancellationToken)
    {
        var fired = new List<(AlertRule Rule, AlertFiring Firing)>();
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Two API instances must not both fire the same rule.
            if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({AdvisoryLock}) AS \"Value\"").SingleAsync(cancellationToken))
            {
                return [];
            }

            var now = clock.GetUtcNow();
            var rules = await db.AlertRules.Where(r => r.Enabled).ToListAsync(cancellationToken);
            foreach (var rule in rules.Where(r => r.IsDue(now)))
            {
                var (count, sample) = await queries.CountAsync(now - rule.Window, now, rule.MinLevel, rule.Service, cancellationToken);
                if (count >= rule.Threshold)
                {
                    var firing = rule.Fire(count, sample, now);
                    db.AlertFirings.Add(firing);
                    fired.Add((rule, firing));
                    LogFired(logger, rule.Name, count, rule.Threshold);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var (rule, firing) in fired.Where(f => f.Rule.Channel != AlertChannel.None))
        {
            try
            {
                await notifier.NotifyAsync(rule, firing, cancellationToken);
                firing.MarkDelivered();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                firing.MarkFailed($"{ex.GetType().Name}: {ex.Message}");
                LogDeliveryFailed(logger, ex, rule.Name);
            }
        }

        if (fired.Count > 0)
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return fired.Select(f => f.Firing).ToList();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alert {RuleName} fired: {Count} events (threshold {Threshold})")]
    private static partial void LogFired(ILogger logger, string ruleName, long count, int threshold);

    [LoggerMessage(Level = LogLevel.Error, Message = "Alert {RuleName} could not be delivered")]
    private static partial void LogDeliveryFailed(ILogger logger, Exception exception, string ruleName);
}

public sealed partial class AlertEvaluator(IServiceScopeFactory scopes, IOptions<AlertOptions> options, TimeProvider clock, ILogger<AlertEvaluator> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AlertEvaluation>().RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }

            await Task.Delay(options.Value.EvaluationInterval, clock, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Evaluating alerts failed; it will run again")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
