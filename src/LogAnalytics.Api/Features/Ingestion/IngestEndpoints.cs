using System.Security.Claims;
using System.Text;
using LogAnalytics.Api.Features.Auth;
using LogAnalytics.Core;
using LogAnalytics.Core.Events;
using LogAnalytics.Infrastructure.Ingestion;
using LogAnalytics.Infrastructure.Partitions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace LogAnalytics.Api.Features.Ingestion;

public sealed record IngestResponse(int Accepted, List<RejectedEvent> Rejected);

public sealed record IngestionStatsResponse(int Queued, int Capacity, long Accepted, long Written, long RefusedWhileFull);

internal static class IngestEndpoints
{
    public const string RateLimit = "ingest";
    public const string ClefContentType = "application/vnd.serilog.clef";
    private const int MaxBodyBytes = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/events", Ingest)
            .WithTags("Ingestion")
            .RequireAuthorization(AuthSetup.IngestPolicy)
            .RequireRateLimiting(RateLimit)
            .Accepts<string>("application/json", ClefContentType, "application/x-ndjson")
            .WithSummary("Send logs")
            .WithDescription("""
                A JSON array of events (`timestamp`, `level`, `message` or `messageTemplate`, and optionally
                `exception`, `service`, `traceId`, `spanId`, `properties`), or Serilog's compact format (CLEF) with
                `Content-Type: application/vnd.serilog.clef`, one event per line. Up to 1000 events. Events that
                cannot be stored are listed by position; the rest are accepted. `429` means the queue is full:
                send the same batch again after `Retry-After` seconds.
                """);

        // What a Seq client (Serilog.Sinks.Seq, Seq.Extensions.Logging) posts to, so existing applications only change the URL.
        foreach (var path in new[] { "/api/events/raw", "/ingest/clef" })
        {
            app.MapPost(path, IngestSeq)
                .WithTags("Ingestion")
                .RequireAuthorization(AuthSetup.IngestPolicy)
                .RequireRateLimiting(RateLimit)
                .WithSummary("Send logs the way a Seq client does")
                .WithDescription("CLEF lines with the key in `X-Seq-ApiKey`. Answers like Seq does.");
        }

        app.MapGet("/api/ingestion/stats", Stats)
            .WithTags("Ingestion")
            .RequireAuthorization()
            .WithSummary("How the ingestion queue is doing");

        return app;
    }

    private static async Task<Results<Accepted<IngestResponse>, BadRequest<IngestResponse>, StatusCodeHttpResult>> Ingest(
        HttpContext context, ClaimsPrincipal source, IngestionQueue queue, IOptions<RetentionOptions> retention, TimeProvider clock)
    {
        var outcome = await ReadAndEnqueueAsync(context, source, queue, retention.Value, clock);
        return outcome switch
        {
            { Full: true } => TooMany(context),
            { Response.Accepted: 0 } and { Response.Rejected.Count: > 0 } => TypedResults.BadRequest(outcome.Response),
            _ => TypedResults.Accepted((string?)null, outcome.Response),
        };
    }

    private static async Task<IResult> IngestSeq(
        HttpContext context, ClaimsPrincipal source, IngestionQueue queue, IOptions<RetentionOptions> retention, TimeProvider clock)
    {
        var outcome = await ReadAndEnqueueAsync(context, source, queue, retention.Value, clock, forceClef: true);
        return outcome.Full
            ? TooMany(context)
            : Results.Json(new { MinimumLevelAccepted = (string?)null }, statusCode: StatusCodes.Status201Created);
    }

    private static Ok<IngestionStatsResponse> Stats(IngestionQueue queue, LogWriter writer) =>
        TypedResults.Ok(new IngestionStatsResponse(queue.Count, queue.Capacity, queue.Accepted, writer.Written, queue.Refused));

    private static async Task<(IngestResponse Response, bool Full)> ReadAndEnqueueAsync(
        HttpContext context, ClaimsPrincipal source, IngestionQueue queue, RetentionOptions retention, TimeProvider clock, bool forceClef = false)
    {
        var body = await ReadBodyAsync(context.Request);
        var clef = forceClef || IsClef(context.Request.ContentType);
        var (events, rejected) = clef ? EventReader.ReadClef(body) : EventReader.ReadJsonArray(body);
        if (events.Count + rejected.Count > EventRules.MaxEventsPerRequest)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"Send at most {EventRules.MaxEventsPerRequest} events per request.");
        }

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var oldest = new DateTimeOffset(today.AddDays(-(retention.Days - 1)).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var newest = new DateTimeOffset(today.AddDays(retention.DaysAhead + 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var sourceId = Guid.Parse(source.FindFirstValue(SourceKeyHandler.SourceIdClaim)!);
        var sourceName = source.FindFirstValue(SourceKeyHandler.SourceNameClaim)!;

        var records = new List<LogRecord>(events.Count);
        foreach (var (index, incoming) in events)
        {
            var (stored, reason) = EventRules.Check(incoming, sourceName, oldest, newest);
            if (stored is null)
            {
                rejected.Add(new RejectedEvent(index, reason!));
            }
            else
            {
                records.Add(new LogRecord(sourceId, now, stored));
            }
        }

        rejected.Sort((a, b) => a.Index.CompareTo(b.Index));
        var response = new IngestResponse(records.Count, rejected);
        return records.Count == 0 || queue.TryEnqueueAll(records) ? (response, false) : (response, true);
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A request is at most {MaxBodyBytes / 1024 / 1024} MB.");
        }

        // Kestrel stops reading past this, also when the sender does not say how long the body is.
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = MaxBodyBytes;
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
    }

    private static bool IsClef(string? contentType) =>
        contentType is not null
        && (contentType.StartsWith(ClefContentType, StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("application/x-ndjson", StringComparison.OrdinalIgnoreCase));

    private static StatusCodeHttpResult TooMany(HttpContext context)
    {
        context.Response.Headers.RetryAfter = "1";
        return TypedResults.StatusCode(StatusCodes.Status429TooManyRequests);
    }
}
