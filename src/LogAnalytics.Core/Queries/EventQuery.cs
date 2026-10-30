using LogAnalytics.Core.Events;

namespace LogAnalytics.Core.Queries;

/// <summary>A property that must have a given value, such as <c>OrderId:SIP-1001</c>.</summary>
public sealed record PropertyFilter(string Key, string Value);

/// <summary>What a search asks for. Built and checked by <see cref="QueryParser"/>.</summary>
public sealed record EventQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    LogLevel? MinLevel,
    IReadOnlyList<string> Services,
    string? Text,
    string? TraceId,
    IReadOnlyList<PropertyFilter> Properties,
    EventCursor? After,
    int Limit);

/// <summary>A position in the newest-first order: the last event seen, by time and id.</summary>
public sealed record EventCursor(DateTimeOffset Timestamp, long Id)
{
    public string Encode() =>
        Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{Timestamp.UtcTicks}:{Id}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static EventCursor? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
        {
            return null;
        }

        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - (padded.Length % 4)) % 4);
            var parts = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(padded)).Split(':');
            return parts.Length == 2 && long.TryParse(parts[0], out var ticks) && long.TryParse(parts[1], out var id)
                && ticks >= DateTimeOffset.MinValue.UtcTicks && ticks <= DateTimeOffset.MaxValue.UtcTicks
                ? new EventCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
