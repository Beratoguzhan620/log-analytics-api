namespace LogAnalytics.Infrastructure.Partitions;

public sealed class RetentionOptions
{
    public const string Section = "Retention";

    /// <summary>Days of logs to keep. Older daily partitions are dropped.</summary>
    public int Days { get; set; } = 14;

    /// <summary>How far ahead partitions are created, so a clock that runs a little fast still finds one.</summary>
    public int DaysAhead { get; set; } = 2;

    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(1);
}
