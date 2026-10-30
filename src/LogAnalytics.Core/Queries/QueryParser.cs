using System.Globalization;
using LogAnalytics.Core.Events;

namespace LogAnalytics.Core.Queries;

/// <summary>
/// Turns query string values into an <see cref="EventQuery"/>, naming every mistake at once. Time is either
/// <c>from</c>/<c>to</c> or a <c>range</c> back from now (<c>15m</c>, <c>1h</c>, <c>24h</c>, <c>7d</c>).
/// </summary>
public static class QueryParser
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
    public const int MaxTextLength = 200;
    public const int MaxProperties = 5;
    public static readonly TimeSpan DefaultRange = TimeSpan.FromHours(1);

    public static (DateTimeOffset From, DateTimeOffset To) TimeWindow(string? from, string? to, string? range, DateTimeOffset now, List<string> errors)
    {
        if (!string.IsNullOrEmpty(from) || !string.IsNullOrEmpty(to))
        {
            var start = ParseTime(from, "from", errors) ?? now - DefaultRange;
            var end = ParseTime(to, "to", errors) ?? now;
            if (end <= start)
            {
                errors.Add("to must be after from.");
            }

            return (start, end);
        }

        var span = string.IsNullOrEmpty(range) ? DefaultRange : ParseRange(range);
        if (span is null)
        {
            errors.Add("range is a number followed by m, h or d, such as 15m, 24h or 7d.");
            span = DefaultRange;
        }

        return (now - span.Value, now);
    }

    public static EventQuery Parse(
        string? from, string? to, string? range, string? level, IEnumerable<string>? services, string? text, string? traceId,
        IEnumerable<string>? properties, string? cursor, int? limit, DateTimeOffset now)
    {
        var errors = new List<string>();
        var (start, end) = TimeWindow(from, to, range, now, errors);

        LogLevel? minLevel = null;
        if (!string.IsNullOrEmpty(level))
        {
            minLevel = Levels.Parse(level);
            if (minLevel is null)
            {
                errors.Add("level is one of Verbose, Debug, Information, Warning, Error, Fatal.");
            }
        }

        var serviceList = (services ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.Ordinal).ToList();

        var trimmedText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (trimmedText?.Length > MaxTextLength)
        {
            errors.Add($"q has at most {MaxTextLength} characters.");
        }

        var propertyFilters = new List<PropertyFilter>();
        foreach (var raw in properties ?? [])
        {
            var colon = raw.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon == raw.Length - 1)
            {
                errors.Add($"prop '{raw}' is not key:value.");
                continue;
            }

            propertyFilters.Add(new PropertyFilter(raw[..colon].Trim(), raw[(colon + 1)..].Trim()));
        }

        if (propertyFilters.Count > MaxProperties)
        {
            errors.Add($"At most {MaxProperties} property filters.");
        }

        EventCursor? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            after = EventCursor.Decode(cursor);
            if (after is null)
            {
                errors.Add("cursor is not one this API gave out.");
            }
        }

        if (limit is < 1 or > MaxLimit)
        {
            errors.Add($"limit is between 1 and {MaxLimit}.");
        }

        if (errors.Count > 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, string.Join(" ", errors));
        }

        return new EventQuery(start, end, minLevel, serviceList, trimmedText,
            string.IsNullOrWhiteSpace(traceId) ? null : traceId.Trim(), propertyFilters, after, limit ?? DefaultLimit);
    }

    /// <summary>About sixty bars for any window, rounded to a step people read easily.</summary>
    public static TimeSpan BucketFor(TimeSpan window)
    {
        TimeSpan[] steps =
        [
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
            TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12), TimeSpan.FromDays(1),
        ];
        var target = window / 60;
        return steps.FirstOrDefault(s => s >= target, steps[^1]);
    }

    public static TimeSpan? ParseRange(string range)
    {
        if (range.Length < 2 || !int.TryParse(range[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount < 1)
        {
            return null;
        }

        TimeSpan? span = range[^1] switch
        {
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => null,
        };
        return span is { } s && s <= TimeSpan.FromDays(31) ? s : null;
    }

    private static DateTimeOffset? ParseTime(string? value, string name, List<string> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        errors.Add($"{name} is an ISO 8601 time.");
        return null;
    }
}
