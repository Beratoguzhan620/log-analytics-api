using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LogAnalytics.Infrastructure.Partitions;

public sealed record PartitionChanges(IReadOnlyList<string> Created, IReadOnlyList<string> Dropped);

/// <summary>
/// One partition per UTC day: <c>log_events_y2026m10d30</c>. Partitions are created ahead of time for every day
/// in the retention window and a few days beyond, and dropped once they fall out of it. Dropping a partition
/// removes a day of logs at once, without the dead rows a <c>DELETE</c> would leave behind.
/// </summary>
public sealed class PartitionMaintenance(Persistence.LogsDbContext db, IOptions<RetentionOptions> options, TimeProvider clock)
{
    public const string Parent = "log_events";
    private const string NamePrefix = "log_events_y";

    /// <summary>A number of our own, so two API instances never maintain partitions at the same time.</summary>
    private const long AdvisoryLock = 0x4C_4F_47_53;

    public static string NameFor(DateOnly day) => string.Create(CultureInfo.InvariantCulture, $"{NamePrefix}{day:yyyy}m{day:MM}d{day:dd}");

    public static DateOnly? DayOf(string partition) =>
        partition.StartsWith(NamePrefix, StringComparison.Ordinal)
        && DateOnly.TryParseExact(partition[NamePrefix.Length..], "yyyy'm'MM'd'dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    /// <summary>The first day still kept; anything before it is dropped.</summary>
    public DateOnly OldestKeptDay => Today.AddDays(-(options.Value.Days - 1));

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<PartitionChanges> RunAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var locked = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({AdvisoryLock}) AS \"Value\"")
            .SingleAsync(cancellationToken);
        if (!locked)
        {
            return new PartitionChanges([], []);
        }

        var existing = (await ListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var created = new List<string>();
        for (var day = OldestKeptDay; day <= Today.AddDays(options.Value.DaysAhead); day = day.AddDays(1))
        {
            var name = NameFor(day);
            if (existing.Contains(name))
            {
                continue;
            }

            var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var to = from.AddDays(1);
            await ExecuteDdlAsync(string.Create(CultureInfo.InvariantCulture,
                $"CREATE TABLE {name} PARTITION OF {Parent} FOR VALUES FROM ('{from:yyyy-MM-dd}T00:00:00Z') TO ('{to:yyyy-MM-dd}T00:00:00Z')"),
                cancellationToken);
            created.Add(name);
        }

        var dropped = new List<string>();
        foreach (var name in existing)
        {
            if (DayOf(name) is { } day && day < OldestKeptDay)
            {
                await ExecuteDdlAsync("DROP TABLE " + name, cancellationToken);
                dropped.Add(name);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new PartitionChanges(created, dropped);
    }

    /// <summary>
    /// DDL cannot take parameters. Every name and bound passed here is built from a date in this class, or is a
    /// catalog name that <see cref="DayOf"/> has matched against the partition pattern, never from input.
    /// </summary>
    private Task<int> ExecuteDdlAsync(string sql, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(sql, cancellationToken);

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<string>($"""
            SELECT c.relname AS "Value"
            FROM pg_inherits i
            JOIN pg_class c ON c.oid = i.inhrelid
            JOIN pg_class p ON p.oid = i.inhparent
            WHERE p.relname = {Parent}
            ORDER BY c.relname
            """).ToListAsync(cancellationToken);
}
