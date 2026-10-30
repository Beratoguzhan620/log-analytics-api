using LogAnalytics.Api.Common;
using LogAnalytics.Api.Features.Auth;
using LogAnalytics.Core;
using LogAnalytics.Core.Sources;
using LogAnalytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LogAnalytics.Api.Features.Sources;

public sealed record CreateSourceRequest(string Name);

public sealed record SourceResponse(Guid Id, string Name, string KeyPrefix, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt)
{
    public static SourceResponse Create(Source source) => new(source.Id, source.Name, source.KeyPrefix, source.CreatedAt, source.RevokedAt);
}

/// <summary>Returned once, when the key is created. The service keeps only a hash.</summary>
public sealed record CreatedSourceResponse(Guid Id, string Name, string KeyPrefix, DateTimeOffset CreatedAt, string ApiKey);

internal static partial class SourceEndpoints
{
    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sources").WithTags("Sources");
        group.MapGet("/", List).RequireAuthorization().WithSummary("Applications that send logs");
        group.MapPost("/", Create)
            .RequireAuthorization(AuthSetup.AdminPolicy)
            .WithSummary("Add a source and its API key (admin)")
            .WithDescription("The key is in the response only; store it right away. It can send logs, nothing else.");
        group.MapPost("/{id:guid}/revoke", Revoke)
            .RequireAuthorization(AuthSetup.AdminPolicy)
            .WithSummary("Revoke a source's key (admin)");
        return app;
    }

    private static async Task<Ok<List<SourceResponse>>> List(LogsDbContext db, CancellationToken ct) =>
        TypedResults.Ok((await db.Sources.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct)).Select(SourceResponse.Create).ToList());

    private static async Task<Results<Created<CreatedSourceResponse>, ValidationProblem>> Create(
        CreateSourceRequest request, LogsDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = new Validation().Require(!string.IsNullOrWhiteSpace(request.Name), "name", "Give the source a name.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var (source, key) = Source.Create(request.Name, clock.GetUtcNow());
        db.Sources.Add(source);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_sources_name"))
        {
            throw new DomainException(ErrorCodes.NameTaken, "A source with this name already exists.");
        }

        LogCreated(loggers.CreateLogger(typeof(SourceEndpoints)), source.Name, source.KeyPrefix);
        return TypedResults.Created($"/api/sources/{source.Id}",
            new CreatedSourceResponse(source.Id, source.Name, source.KeyPrefix, source.CreatedAt, key));
    }

    private static async Task<Ok<SourceResponse>> Revoke(Guid id, LogsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var source = await db.Sources.SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no source with this id.");
        source.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(SourceResponse.Create(source));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {SourceName} created with key prefix {KeyPrefix}")]
    private static partial void LogCreated(ILogger logger, string sourceName, string keyPrefix);
}
