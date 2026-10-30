namespace LogAnalytics.Infrastructure.Alerts;

public sealed class AlertOptions
{
    public const string Section = "Alerts";

    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(30);

    public NotificationServiceOptions NotificationService { get; set; } = new();
}

/// <summary>Where to send alerts by email: a notification-service, its key and who gets them.</summary>
public sealed class NotificationServiceOptions
{
    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public string Template { get; set; } = "log-alert";

    public string RecipientId { get; set; } = "on-call";

    public string? Email { get; set; }

    public string Language { get; set; } = "en";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Email);
}
