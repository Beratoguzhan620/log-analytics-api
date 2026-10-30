namespace LogAnalytics.Core.Events;

/// <summary>What is stored, and what is refused or shortened on the way in.</summary>
public static class EventRules
{
    public const int MaxEventsPerRequest = 1000;
    public const int MaxMessageLength = 16 * 1024;
    public const int MaxExceptionLength = 32 * 1024;
    public const int MaxPropertiesLength = 32 * 1024;
    public const int MaxServiceLength = 100;
    public const int MaxIdLength = 64;
    public const string UnknownService = "unknown";

    /// <summary>
    /// Checks one event against the window the partitions cover (<paramref name="oldest"/> to
    /// <paramref name="newest"/>) and the size limits. Very long messages and exceptions are cut rather than lost.
    /// </summary>
    public static (StoredEvent? Event, string? Reason) Check(IncomingEvent incoming, string defaultService, DateTimeOffset oldest, DateTimeOffset newest)
    {
        if (incoming.Timestamp < oldest || incoming.Timestamp >= newest)
        {
            return (null, $"The time is outside what is kept ({oldest:yyyy-MM-dd} to {newest:yyyy-MM-dd}).");
        }

        var properties = incoming.Properties.ToJsonString();
        if (properties.Length > MaxPropertiesLength)
        {
            return (null, $"The properties are larger than {MaxPropertiesLength / 1024} KB.");
        }

        var service = Clean(incoming.Service, MaxServiceLength) ?? Clean(defaultService, MaxServiceLength) ?? UnknownService;
        return (new StoredEvent(
            incoming.Timestamp,
            incoming.Level,
            service,
            Cut(incoming.Message, MaxMessageLength)!,
            Cut(incoming.MessageTemplate, MaxMessageLength),
            Cut(incoming.Exception, MaxExceptionLength),
            Clean(incoming.TraceId, MaxIdLength),
            Clean(incoming.SpanId, MaxIdLength),
            properties), null);
    }

    private static string? Cut(string? value, int max) =>
        value is null || value.Length <= max ? value : string.Concat(value.AsSpan(0, max), " …[cut]");

    /// <summary>Single-line, printable and bounded: safe in filters, URLs and the dashboard.</summary>
    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value.Trim().Where(c => !char.IsControl(c)).Take(max).ToArray());
        return cleaned.Length == 0 ? null : cleaned;
    }
}

/// <summary>An event ready to be written.</summary>
public sealed record StoredEvent(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Service,
    string Message,
    string? MessageTemplate,
    string? Exception,
    string? TraceId,
    string? SpanId,
    string PropertiesJson);
