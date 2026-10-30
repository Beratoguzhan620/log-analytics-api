using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogAnalytics.Infrastructure.Partitions;

/// <summary>Keeps the partitions in shape: once at startup, before anything is ingested, and then every hour.</summary>
public sealed partial class PartitionMaintainer(
    IServiceScopeFactory scopes,
    IOptions<RetentionOptions> options,
    TimeProvider clock,
    ILogger<PartitionMaintainer> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RunOnceAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(options.Value.CheckInterval, clock, stoppingToken);
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var changes = await scope.ServiceProvider.GetRequiredService<PartitionMaintenance>().RunAsync(cancellationToken);
        if (changes.Created.Count > 0 || changes.Dropped.Count > 0)
        {
            LogChanged(logger, changes.Created.Count, changes.Dropped.Count, string.Join(", ", changes.Dropped));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Partitions: {Created} created, {Dropped} dropped ({DroppedNames})")]
    private static partial void LogChanged(ILogger logger, int created, int dropped, string droppedNames);

    [LoggerMessage(Level = LogLevel.Error, Message = "Partition maintenance failed; it will run again")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
