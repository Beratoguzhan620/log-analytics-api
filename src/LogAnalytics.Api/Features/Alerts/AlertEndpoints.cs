using LogAnalytics.Api.Common;
using LogAnalytics.Api.Features.Auth;
using LogAnalytics.Core;
using LogAnalytics.Core.Alerts;
using LogAnalytics.Core.Events;
using LogAnalytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LogAnalytics.Api.Features.Alerts;

public sealed record AlertRuleRequest(
    string Name, int Threshold, int WindowMinutes, string? Service = null, LogLevel MinLevel = LogLevel.Error,
    int CooldownMinutes = 15, AlertChannel Channel = AlertChannel.None, string? WebhookUrl = null, bool Enabled = true);

public sealed record AlertRuleResponse(
    Guid Id, string Name, string? Service, LogLevel MinLevel, int Threshold, int WindowMinutes, int CooldownMinutes,
    AlertChannel Channel, string? WebhookUrl, bool Enabled, DateTimeOffset CreatedAt, DateTimeOffset? LastFiredAt)
{
    public static AlertRuleResponse Create(AlertRule r) => new(r.Id, r.Name, r.Service, r.MinLevel, r.Threshold,
        (int)r.Window.TotalMinutes, (int)r.Cooldown.TotalMinutes, r.Channel, r.WebhookUrl, r.Enabled, r.CreatedAt, r.LastFiredAt);
}

public sealed record AlertFiringResponse(
    Guid Id, Guid RuleId, string RuleName, DateTimeOffset FiredAt, DateTimeOffset WindowStart, long Count, string? SampleMessage,
    AlertChannel Channel, bool? Delivered, string? DeliveryError)
{
    public static AlertFiringResponse Create(AlertFiring f) => new(f.Id, f.RuleId, f.RuleName, f.FiredAt, f.WindowStart, f.Count,
        f.SampleMessage, f.Channel, f.Delivered, f.DeliveryError);
}

internal static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alerts").WithTags("Alerts");
        group.MapGet("/rules", ListRules).RequireAuthorization().WithSummary("Alert rules");
        group.MapGet("/firings", ListFirings).RequireAuthorization().WithSummary("Recent firings, newest first");
        group.MapPost("/rules", Create).RequireAuthorization(AuthSetup.AdminPolicy)
            .WithSummary("Add a rule (admin)")
            .WithDescription("Fires when at least `threshold` events of `minLevel` or worse arrive within `windowMinutes`, then stays quiet for `cooldownMinutes`.");
        group.MapPut("/rules/{id:guid}", Update).RequireAuthorization(AuthSetup.AdminPolicy).WithSummary("Change a rule (admin)");
        group.MapDelete("/rules/{id:guid}", Delete).RequireAuthorization(AuthSetup.AdminPolicy).WithSummary("Remove a rule and its firings (admin)");
        return app;
    }

    private static async Task<Ok<List<AlertRuleResponse>>> ListRules(LogsDbContext db, CancellationToken ct) =>
        TypedResults.Ok((await db.AlertRules.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct)).Select(AlertRuleResponse.Create).ToList());

    private static async Task<Ok<List<AlertFiringResponse>>> ListFirings(LogsDbContext db, CancellationToken ct, int limit = 50) =>
        TypedResults.Ok((await db.AlertFirings.AsNoTracking().OrderByDescending(f => f.FiredAt).Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct))
            .Select(AlertFiringResponse.Create).ToList());

    private static async Task<Results<Created<AlertRuleResponse>, ValidationProblem>> Create(
        AlertRuleRequest request, LogsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
        {
            return problem;
        }

        var rule = AlertRule.Create(request.Name, request.Service, request.MinLevel, request.Threshold,
            TimeSpan.FromMinutes(request.WindowMinutes), TimeSpan.FromMinutes(request.CooldownMinutes), request.Channel, request.WebhookUrl, clock.GetUtcNow());
        db.AlertRules.Add(rule);
        await SaveAsync(db, ct);
        return TypedResults.Created($"/api/alerts/rules/{rule.Id}", AlertRuleResponse.Create(rule));
    }

    private static async Task<Results<Ok<AlertRuleResponse>, ValidationProblem>> Update(
        Guid id, AlertRuleRequest request, LogsDbContext db, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
        {
            return problem;
        }

        var rule = await db.AlertRules.SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no rule with this id.");
        rule.Update(request.Name, request.Service, request.MinLevel, request.Threshold, TimeSpan.FromMinutes(request.WindowMinutes),
            TimeSpan.FromMinutes(request.CooldownMinutes), request.Channel, request.WebhookUrl, request.Enabled);
        await SaveAsync(db, ct);
        return TypedResults.Ok(AlertRuleResponse.Create(rule));
    }

    private static async Task<NoContent> Delete(Guid id, LogsDbContext db, CancellationToken ct)
    {
        var deleted = await db.AlertRules.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? throw new DomainException(ErrorCodes.NotFound, "There is no rule with this id.") : TypedResults.NoContent();
    }

    private static ValidationProblem? Validate(AlertRuleRequest request)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Name), "name", "Give the rule a name.")
            .Require(Enum.IsDefined(request.MinLevel), "minLevel", "minLevel is one of Verbose, Debug, Information, Warning, Error, Fatal.");
        return validation.IsValid ? null : validation.ToProblem();
    }

    private static async Task SaveAsync(LogsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.NameTaken, "A rule with this name already exists.");
        }
    }
}
