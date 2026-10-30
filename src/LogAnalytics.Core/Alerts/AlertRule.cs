using LogAnalytics.Core.Events;

namespace LogAnalytics.Core.Alerts;

public enum AlertChannel
{
    /// <summary>Only recorded and shown on the dashboard.</summary>
    None,

    /// <summary>A JSON POST to the rule's URL.</summary>
    Webhook,

    /// <summary>An email through the notification-service.</summary>
    NotificationService,
}

/// <summary>"At least <see cref="Threshold"/> events of <see cref="MinLevel"/> or worse within <see cref="Window"/>."</summary>
public sealed class AlertRule
{
    public const int MaxNameLength = 100;
    public static readonly TimeSpan MinWindow = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxWindow = TimeSpan.FromHours(24);

    private AlertRule()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>One service, or every service when empty.</summary>
    public string? Service { get; private set; }

    public LogLevel MinLevel { get; private set; }

    public int Threshold { get; private set; }

    public TimeSpan Window { get; private set; }

    /// <summary>Quiet time after a firing, so one incident is one alert and not one a minute.</summary>
    public TimeSpan Cooldown { get; private set; }

    public AlertChannel Channel { get; private set; }

    public string? WebhookUrl { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastFiredAt { get; private set; }

    public static AlertRule Create(
        string name, string? service, LogLevel minLevel, int threshold, TimeSpan window, TimeSpan cooldown,
        AlertChannel channel, string? webhookUrl, DateTimeOffset now)
    {
        var rule = new AlertRule { Id = Guid.CreateVersion7(now), CreatedAt = now, Enabled = true };
        rule.Update(name, service, minLevel, threshold, window, cooldown, channel, webhookUrl, enabled: true);
        return rule;
    }

    public void Update(
        string name, string? service, LogLevel minLevel, int threshold, TimeSpan window, TimeSpan cooldown,
        AlertChannel channel, string? webhookUrl, bool enabled)
    {
        var errors = new List<string>();
        var trimmed = name.Trim();
        if (trimmed.Length is < 2 or > MaxNameLength)
        {
            errors.Add($"A name has 2 to {MaxNameLength} characters.");
        }

        if (threshold is < 1 or > 1_000_000)
        {
            errors.Add("The threshold is between 1 and 1,000,000.");
        }

        if (window < MinWindow || window > MaxWindow)
        {
            errors.Add("The window is between 1 minute and 24 hours.");
        }

        if (cooldown < TimeSpan.Zero || cooldown > TimeSpan.FromDays(1))
        {
            errors.Add("The cooldown is between 0 and 24 hours.");
        }

        if (!Enum.IsDefined(channel))
        {
            errors.Add("The channel is None, Webhook or NotificationService.");
        }

        var url = string.IsNullOrWhiteSpace(webhookUrl) ? null : webhookUrl.Trim();
        if (channel == AlertChannel.Webhook
            && (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
        {
            errors.Add("A webhook needs an http or https URL.");
        }

        if (errors.Count > 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, string.Join(" ", errors));
        }

        Name = trimmed;
        Service = string.IsNullOrWhiteSpace(service) ? null : service.Trim();
        MinLevel = minLevel;
        Threshold = threshold;
        Window = window;
        Cooldown = cooldown;
        Channel = channel;
        WebhookUrl = channel == AlertChannel.Webhook ? url : null;
        Enabled = enabled;
    }

    /// <summary>Whether the rule may be checked now: enabled and not cooling down after a firing.</summary>
    public bool IsDue(DateTimeOffset now) => Enabled && (LastFiredAt is null || now - LastFiredAt.Value >= Cooldown);

    public AlertFiring Fire(long count, string? sample, DateTimeOffset now)
    {
        LastFiredAt = now;
        return AlertFiring.Create(this, count, sample, now);
    }
}
