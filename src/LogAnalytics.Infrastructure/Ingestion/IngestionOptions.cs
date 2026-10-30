namespace LogAnalytics.Infrastructure.Ingestion;

public sealed class IngestionOptions
{
    public const string Section = "Ingestion";

    /// <summary>Events waiting to be written. When it is full, senders are asked to come back later.</summary>
    public int QueueCapacity { get; set; } = 100_000;

    /// <summary>Events written in one <c>COPY</c>.</summary>
    public int BatchSize { get; set; } = 2_000;

    /// <summary>How long the writer waits for a batch to fill before writing what it has.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Requests per second one source may send.</summary>
    public int RequestsPerSecondPerSource { get; set; } = 50;
}
