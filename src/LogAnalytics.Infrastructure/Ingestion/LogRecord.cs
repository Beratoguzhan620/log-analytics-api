using LogAnalytics.Core.Events;

namespace LogAnalytics.Infrastructure.Ingestion;

/// <summary>One row of <c>log_events</c>, as the writer copies it.</summary>
public sealed record LogRecord(Guid SourceId, DateTimeOffset ReceivedAt, StoredEvent Event);
