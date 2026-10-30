using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using LogAnalytics.Core.Events;
using LogAnalytics.Core.Queries;
using LogAnalytics.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace LogAnalytics.Infrastructure.Queries;

public sealed record EventRow(
    long Id, DateTimeOffset Timestamp, LogLevel Level, string Service, string Message, string? MessageTemplate,
    string? Exception, string? TraceId, string? SpanId, string Properties, string? SourceName);

public sealed record VolumeRow(DateTimeOffset Bucket, LogLevel Level, long Count);

public sealed record ErrorGroupRow(string Template, string Service, long Count, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, string SampleMessage, string? SampleException);

public sealed record ServiceRow(string Service, long Total, long Warnings, long Errors, DateTimeOffset LastSeen);

/// <summary>
/// The read side, in plain SQL. Every filter is a parameter; only the shape of the WHERE clause changes.
/// Each query is bounded by time, so PostgreSQL only opens the partitions that can match.
/// </summary>
public sealed class LogQueries(LogsDataSource dataSource)
{
    private const string Columns = """
        e.id, e."timestamp", e.level, e.service, e.message, e.message_template, e.exception, e.trace_id, e.span_id, e.properties::text, s.name
        """;

    public async Task<(List<EventRow> Rows, EventCursor? Next)> SearchAsync(EventQuery query, CancellationToken ct)
    {
        await using var command = dataSource.Value.CreateCommand();
        var where = Where(command, query.From, query.To, query.MinLevel, query.Services, query.Text, query.TraceId, query.Properties);
        if (query.After is { } after)
        {
            where.Append(""" AND (e."timestamp", e.id) < (@after_ts, @after_id)""");
            command.Parameters.Add(new NpgsqlParameter("after_ts", NpgsqlDbType.TimestampTz) { Value = after.Timestamp.UtcDateTime });
            command.Parameters.Add(new NpgsqlParameter("after_id", after.Id));
        }

        command.CommandText = $"""
            SELECT {Columns}
            FROM log_events e LEFT JOIN sources s ON s.id = e.source_id
            WHERE {where}
            ORDER BY e."timestamp" DESC, e.id DESC
            LIMIT @limit
            """;
        command.Parameters.Add(new NpgsqlParameter("limit", query.Limit + 1));

        var rows = await ReadEventsAsync(command, ct);
        var next = rows.Count > query.Limit ? new EventCursor(rows[query.Limit - 1].Timestamp, rows[query.Limit - 1].Id) : null;
        return (rows.Take(query.Limit).ToList(), next);
    }

    public async Task<EventRow?> GetAsync(DateTimeOffset timestamp, long id, CancellationToken ct)
    {
        await using var command = dataSource.Value.CreateCommand($"""
            SELECT {Columns}
            FROM log_events e LEFT JOIN sources s ON s.id = e.source_id
            WHERE e."timestamp" = @ts AND e.id = @id
            """);
        command.Parameters.Add(new NpgsqlParameter("ts", NpgsqlDbType.TimestampTz) { Value = timestamp.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("id", id));
        return (await ReadEventsAsync(command, ct)).SingleOrDefault();
    }

    /// <summary>Every event of one trace across services, oldest first, within the kept window.</summary>
    public async Task<List<EventRow>> TraceAsync(string traceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var command = dataSource.Value.CreateCommand($"""
            SELECT {Columns}
            FROM log_events e LEFT JOIN sources s ON s.id = e.source_id
            WHERE e.trace_id = @trace AND e."timestamp" >= @from AND e."timestamp" < @to
            ORDER BY e."timestamp", e.id
            LIMIT 500
            """);
        command.Parameters.Add(new NpgsqlParameter("trace", traceId));
        AddWindow(command, from, to);
        return await ReadEventsAsync(command, ct);
    }

    public async Task<List<VolumeRow>> VolumeAsync(DateTimeOffset from, DateTimeOffset to, TimeSpan bucket, IReadOnlyList<string> services, CancellationToken ct)
    {
        await using var command = dataSource.Value.CreateCommand();
        var where = Where(command, from, to, null, services, null, null, []);
        command.CommandText = $"""
            SELECT date_bin(@bucket, e."timestamp", @origin) AS bucket, e.level, count(*)
            FROM log_events e
            WHERE {where}
            GROUP BY 1, 2
            ORDER BY 1, 2
            """;
        command.Parameters.Add(new NpgsqlParameter("bucket", bucket));
        command.Parameters.Add(new NpgsqlParameter("origin", NpgsqlDbType.TimestampTz) { Value = DateTime.UnixEpoch });

        var rows = new List<VolumeRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new VolumeRow(reader.GetFieldValue<DateTimeOffset>(0), (LogLevel)reader.GetInt16(1), reader.GetInt64(2)));
        }

        return rows;
    }

    /// <summary>Errors grouped by what kind of error they are: the message template, or the message when there is none.</summary>
    public async Task<List<ErrorGroupRow>> TopErrorsAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyList<string> services, int limit, CancellationToken ct)
    {
        await using var command = dataSource.Value.CreateCommand();
        var where = Where(command, from, to, LogLevel.Error, services, null, null, []);
        command.CommandText = $"""
            SELECT coalesce(e.message_template, e.message) AS template, e.service, count(*),
                   min(e."timestamp"), max(e."timestamp"),
                   (array_agg(e.message ORDER BY e."timestamp" DESC))[1],
                   (array_agg(split_part(e.exception, E'\n', 1) ORDER BY e."timestamp" DESC) FILTER (WHERE e.exception IS NOT NULL))[1]
            FROM log_events e
            WHERE {where}
            GROUP BY 1, 2
            ORDER BY 3 DESC, 5 DESC
            LIMIT @limit
            """;
        command.Parameters.Add(new NpgsqlParameter("limit", limit));

        var rows = new List<ErrorGroupRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new ErrorGroupRow(reader.GetString(0), reader.GetString(1), reader.GetInt64(2),
                reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return rows;
    }

    public async Task<List<ServiceRow>> ServicesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var command = dataSource.Value.CreateCommand("""
            SELECT e.service, count(*),
                   count(*) FILTER (WHERE e.level = 3),
                   count(*) FILTER (WHERE e.level >= 4),
                   max(e."timestamp")
            FROM log_events e
            WHERE e."timestamp" >= @from AND e."timestamp" < @to
            GROUP BY 1
            ORDER BY 2 DESC
            """);
        AddWindow(command, from, to);

        var rows = new List<ServiceRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new ServiceRow(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return rows;
    }

    /// <summary>How many matching events there are in a window; the alert evaluator asks this.</summary>
    public async Task<(long Count, string? Sample)> CountAsync(DateTimeOffset from, DateTimeOffset to, LogLevel minLevel, string? service, CancellationToken ct)
    {
        await using var command = dataSource.Value.CreateCommand();
        var where = Where(command, from, to, minLevel, service is null ? [] : [service], null, null, []);
        command.CommandText = $"""
            SELECT count(*), (array_agg(e.message ORDER BY e."timestamp" DESC))[1]
            FROM log_events e
            WHERE {where}
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static StringBuilder Where(
        NpgsqlCommand command, DateTimeOffset from, DateTimeOffset to, LogLevel? minLevel, IReadOnlyList<string> services,
        string? text, string? traceId, IReadOnlyList<PropertyFilter> properties)
    {
        var where = new StringBuilder("""e."timestamp" >= @from AND e."timestamp" < @to""");
        AddWindow(command, from, to);

        if (minLevel is { } level)
        {
            where.Append(" AND e.level >= @level");
            command.Parameters.Add(new NpgsqlParameter("level", (short)level));
        }

        if (services.Count > 0)
        {
            where.Append(" AND e.service = ANY(@services)");
            command.Parameters.Add(new NpgsqlParameter("services", services.ToArray()));
        }

        if (text is not null)
        {
            // websearch_to_tsquery understands "quoted phrases", OR and -exclusions, and never fails on odd input.
            where.Append(" AND e.search @@ websearch_to_tsquery('simple', @text)");
            command.Parameters.Add(new NpgsqlParameter("text", text));
        }

        if (traceId is not null)
        {
            where.Append(" AND e.trace_id = @trace");
            command.Parameters.Add(new NpgsqlParameter("trace", traceId));
        }

        for (var i = 0; i < properties.Count; i++)
        {
            // A value that looks like a number may have been logged as one or as text, so both are accepted.
            var asText = new JsonObject { [properties[i].Key] = properties[i].Value }.ToJsonString();
            if (decimal.TryParse(properties[i].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                var asNumber = new JsonObject { [properties[i].Key] = number }.ToJsonString();
                where.Append(CultureInfo.InvariantCulture, $" AND (e.properties @> @p{i}::jsonb OR e.properties @> @n{i}::jsonb)");
                command.Parameters.Add(new NpgsqlParameter($"n{i}", asNumber));
            }
            else
            {
                where.Append(CultureInfo.InvariantCulture, $" AND e.properties @> @p{i}::jsonb");
            }

            command.Parameters.Add(new NpgsqlParameter($"p{i}", asText));
        }

        return where;
    }

    private static void AddWindow(NpgsqlCommand command, DateTimeOffset from, DateTimeOffset to)
    {
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = from.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = to.UtcDateTime });
    }

    private static async Task<List<EventRow>> ReadEventsAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var rows = new List<EventRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new EventRow(
                reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), (LogLevel)reader.GetInt16(2), reader.GetString(3),
                reader.GetString(4), NullableString(reader, 5), NullableString(reader, 6), NullableString(reader, 7),
                NullableString(reader, 8), reader.GetString(9), NullableString(reader, 10)));
        }

        return rows;
    }

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
