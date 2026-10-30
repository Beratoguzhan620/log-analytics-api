namespace LogAnalytics.Core.Alerts;

/// <summary>One time a rule's threshold was reached, and whether telling someone worked.</summary>
public sealed class AlertFiring
{
    public const int MaxTextLength = 500;

    private AlertFiring()
    {
    }

    public Guid Id { get; private set; }

    public Guid RuleId { get; private set; }

    public string RuleName { get; private set; } = string.Empty;

    public DateTimeOffset FiredAt { get; private set; }

    public DateTimeOffset WindowStart { get; private set; }

    public long Count { get; private set; }

    public string? SampleMessage { get; private set; }

    public AlertChannel Channel { get; private set; }

    public bool? Delivered { get; private set; }

    public string? DeliveryError { get; private set; }

    internal static AlertFiring Create(AlertRule rule, long count, string? sample, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        RuleId = rule.Id,
        RuleName = rule.Name,
        FiredAt = now,
        WindowStart = now - rule.Window,
        Count = count,
        SampleMessage = sample is { Length: > MaxTextLength } ? sample[..MaxTextLength] : sample,
        Channel = rule.Channel,
    };

    public void MarkDelivered() => (Delivered, DeliveryError) = (true, null);

    public void MarkFailed(string error) =>
        (Delivered, DeliveryError) = (false, error.Length > MaxTextLength ? error[..MaxTextLength] : error);
}
