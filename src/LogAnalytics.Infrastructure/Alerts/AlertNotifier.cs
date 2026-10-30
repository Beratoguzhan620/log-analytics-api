using System.Globalization;
using System.Net.Http.Json;
using LogAnalytics.Core.Alerts;
using Microsoft.Extensions.Options;

namespace LogAnalytics.Infrastructure.Alerts;

/// <summary>Tells someone about a firing. Throwing means it did not get through; the firing records why.</summary>
public interface IAlertNotifier
{
    Task NotifyAsync(AlertRule rule, AlertFiring firing, CancellationToken cancellationToken);
}

/// <summary>Sends by the rule's channel: a webhook POST or an email through the notification-service.</summary>
public sealed class AlertNotifier(IHttpClientFactory http, IOptions<AlertOptions> options) : IAlertNotifier
{
    public const string HttpClientName = "alerts";

    public async Task NotifyAsync(AlertRule rule, AlertFiring firing, CancellationToken cancellationToken)
    {
        switch (rule.Channel)
        {
            case AlertChannel.Webhook:
                await SendWebhookAsync(rule, firing, cancellationToken);
                break;
            case AlertChannel.NotificationService:
                await SendThroughNotificationServiceAsync(rule, firing, cancellationToken);
                break;
        }
    }

    private async Task SendWebhookAsync(AlertRule rule, AlertFiring firing, CancellationToken cancellationToken)
    {
        using var client = http.CreateClient(HttpClientName);
        var response = await client.PostAsJsonAsync(rule.WebhookUrl, new
        {
            alert = rule.Name,
            service = rule.Service,
            level = rule.MinLevel.ToString(),
            count = firing.Count,
            threshold = rule.Threshold,
            windowMinutes = rule.Window.TotalMinutes,
            firedAt = firing.FiredAt,
            sample = firing.SampleMessage,
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>The firing id is the idempotency key, so a retry after a timeout never sends a second email.</summary>
    private async Task SendThroughNotificationServiceAsync(AlertRule rule, AlertFiring firing, CancellationToken cancellationToken)
    {
        var settings = options.Value.NotificationService;
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("Alerts:NotificationService is not configured.");
        }

        using var client = http.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BaseUrl!), "/api/notifications"))
        {
            Content = JsonContent.Create(new
            {
                recipient = new { id = settings.RecipientId, email = settings.Email },
                channel = "Email",
                template = settings.Template,
                language = settings.Language,
                variables = new Dictionary<string, string>
                {
                    ["alert"] = rule.Name,
                    ["service"] = rule.Service ?? "all services",
                    ["count"] = firing.Count.ToString(CultureInfo.InvariantCulture),
                    ["minutes"] = rule.Window.TotalMinutes.ToString(CultureInfo.InvariantCulture),
                    ["sample"] = firing.SampleMessage ?? "",
                },
            }),
        };
        request.Headers.Add("X-Api-Key", settings.ApiKey);
        request.Headers.Add("Idempotency-Key", $"log-alert-{firing.Id}");
        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
