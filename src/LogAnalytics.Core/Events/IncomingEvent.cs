using System.Text.Json.Nodes;

namespace LogAnalytics.Core.Events;

/// <summary>An event as read from a request, before it is checked and stored.</summary>
public sealed record IncomingEvent(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Message,
    string? MessageTemplate,
    string? Exception,
    string? Service,
    string? TraceId,
    string? SpanId,
    JsonObject Properties);

/// <summary>An event that could not be read or was refused, by its position in the request.</summary>
public sealed record RejectedEvent(int Index, string Reason);
