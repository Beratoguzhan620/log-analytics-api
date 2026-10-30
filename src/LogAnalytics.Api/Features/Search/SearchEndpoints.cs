using System.Text.Json;
using LogAnalytics.Core;
using LogAnalytics.Core.Events;
using LogAnalytics.Core.Queries;
using LogAnalytics.Infrastructure.Partitions;
using LogAnalytics.Infrastructure.Queries;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LogAnalytics.Api.Features.Search;

public sealed record EventResponse(
    string Key, DateTimeOffset Timestamp, LogLevel Level, string Service, string Message, string? MessageTemplate,
    string? Exception, string? TraceId, string? SpanId, JsonElement Properties, string? Source)
{
    public static EventResponse Create(EventRow row) => new(
        new EventCursor(row.Timestamp, row.Id).Encode(), row.Timestamp, row.Level, row.Service, row.Message, row.MessageTemplate,
        row.Exception, row.TraceId, row.SpanId, JsonDocument.Parse(row.Properties).RootElement.Clone(), row.SourceName);
}

public sealed record EventPage(List<EventResponse> Items, string? NextCursor);

public sealed record VolumePoint(DateTimeOffset Bucket, Dictionary<string, long> Counts, long Total);

public sealed record VolumeResponse(DateTimeOffset From, DateTimeOffset To, TimeSpan Bucket, List<VolumePoint> Points);

public sealed record ErrorGroup(string Template, string Service, long Count, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, string SampleMessage, string? SampleException);

public sealed record ServiceSummary(string Service, long Total, long Warnings, long Errors, double ErrorRate, DateTimeOffset LastSeen);

public sealed record Overview(DateTimeOffset From, DateTimeOffset To, long Total, long Warnings, long Errors, double ErrorRate, List<ServiceSummary> Services);

internal static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/events", Search).WithTags("Search")
            .WithSummary("Search logs, newest first")
            .WithDescription("""
                Time is `from`/`to` (ISO 8601) or `range` back from now (`15m`, `1h`, `24h`, `7d`; one hour by default).
                Filters: `level` (that level or worse), `service` (repeatable), `q` (full-text in messages and exceptions:
                words, `"a phrase"`, `or`, `-exclude`), `traceId`, `prop=Key:Value` (repeatable). Pass `nextCursor` as
                `cursor` for the next page.
                """);
        group.MapGet("/events/{key}", Get).WithTags("Search").WithSummary("One event with all its properties");
        group.MapGet("/traces/{traceId}", Trace).WithTags("Search").WithSummary("Every event of one trace, oldest first");

        group.MapGet("/stats/overview", OverviewAsync).WithTags("Statistics").WithSummary("Totals, error rate and each service");
        group.MapGet("/stats/volume", Volume).WithTags("Statistics")
            .WithSummary("Events per time bucket and level")
            .WithDescription("The bucket is picked to give about sixty bars for the window.");
        group.MapGet("/stats/top-errors", TopErrors).WithTags("Statistics")
            .WithSummary("The most frequent errors")
            .WithDescription("Grouped by message template, so `Order 1 failed` and `Order 2 failed` count as one kind of error.");

        return app;
    }

    private static async Task<Ok<EventPage>> Search(
        LogQueries queries, TimeProvider clock, CancellationToken ct,
        string? from = null, string? to = null, string? range = null, string? level = null,
        [FromQuery(Name = "service")] string[]? service = null, string? q = null, string? traceId = null,
        [FromQuery(Name = "prop")] string[]? prop = null, string? cursor = null, int? limit = null)
    {
        var query = QueryParser.Parse(from, to, range, level, service, q, traceId, prop, cursor, limit, clock.GetUtcNow());
        var (rows, next) = await queries.SearchAsync(query, ct);
        return TypedResults.Ok(new EventPage(rows.Select(EventResponse.Create).ToList(), next?.Encode()));
    }

    private static async Task<Ok<EventResponse>> Get(string key, LogQueries queries, CancellationToken ct)
    {
        var position = EventCursor.Decode(key) ?? throw new DomainException(ErrorCodes.NotFound, "There is no event with this key.");
        var row = await queries.GetAsync(position.Timestamp, position.Id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no event with this key.");
        return TypedResults.Ok(EventResponse.Create(row));
    }

    private static async Task<Ok<List<EventResponse>>> Trace(
        string traceId, LogQueries queries, PartitionMaintenance partitions, TimeProvider clock, CancellationToken ct)
    {
        var from = new DateTimeOffset(partitions.OldestKeptDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rows = await queries.TraceAsync(traceId, from, clock.GetUtcNow().AddDays(1), ct);
        return TypedResults.Ok(rows.Select(EventResponse.Create).ToList());
    }

    private static async Task<Ok<Overview>> OverviewAsync(
        LogQueries queries, TimeProvider clock, CancellationToken ct, string? from = null, string? to = null, string? range = null)
    {
        var (start, end) = Window(from, to, range, clock);
        var services = await queries.ServicesAsync(start, end, ct);
        var total = services.Sum(s => s.Total);
        var errors = services.Sum(s => s.Errors);
        return TypedResults.Ok(new Overview(start, end, total, services.Sum(s => s.Warnings), errors, Rate(errors, total),
            services.Select(s => new ServiceSummary(s.Service, s.Total, s.Warnings, s.Errors, Rate(s.Errors, s.Total), s.LastSeen)).ToList()));
    }

    private static async Task<Ok<VolumeResponse>> Volume(
        LogQueries queries, TimeProvider clock, CancellationToken ct,
        string? from = null, string? to = null, string? range = null, [FromQuery(Name = "service")] string[]? service = null)
    {
        var (start, end) = Window(from, to, range, clock);
        var bucket = QueryParser.BucketFor(end - start);
        var rows = await queries.VolumeAsync(start, end, bucket, service ?? [], ct);

        // Every bucket is present, also the empty ones, so a chart has no gaps to guess about.
        var byBucket = rows.GroupBy(r => r.Bucket).ToDictionary(g => g.Key, g => g.ToList());
        var first = new DateTimeOffset(start.UtcTicks - (start.UtcTicks % bucket.Ticks), TimeSpan.Zero);
        var points = new List<VolumePoint>();
        for (var at = first; at < end; at += bucket)
        {
            var counts = Enum.GetValues<LogLevel>().ToDictionary(l => l.ToString(), _ => 0L);
            foreach (var row in byBucket.GetValueOrDefault(at) ?? [])
            {
                counts[row.Level.ToString()] = row.Count;
            }

            points.Add(new VolumePoint(at, counts, counts.Values.Sum()));
        }

        return TypedResults.Ok(new VolumeResponse(start, end, bucket, points));
    }

    private static async Task<Ok<List<ErrorGroup>>> TopErrors(
        LogQueries queries, TimeProvider clock, CancellationToken ct,
        string? from = null, string? to = null, string? range = null, [FromQuery(Name = "service")] string[]? service = null, int limit = 10)
    {
        var (start, end) = Window(from, to, range, clock);
        var rows = await queries.TopErrorsAsync(start, end, service ?? [], Math.Clamp(limit, 1, 50), ct);
        return TypedResults.Ok(rows.Select(r => new ErrorGroup(r.Template, r.Service, r.Count, r.FirstSeen, r.LastSeen, r.SampleMessage, r.SampleException)).ToList());
    }

    private static (DateTimeOffset From, DateTimeOffset To) Window(string? from, string? to, string? range, TimeProvider clock)
    {
        var errors = new List<string>();
        var window = QueryParser.TimeWindow(from, to, range, clock.GetUtcNow(), errors);
        return errors.Count == 0 ? window : throw new DomainException(ErrorCodes.ValidationFailed, string.Join(" ", errors));
    }

    private static double Rate(long part, long total) => total == 0 ? 0 : Math.Round((double)part / total, 4);
}
