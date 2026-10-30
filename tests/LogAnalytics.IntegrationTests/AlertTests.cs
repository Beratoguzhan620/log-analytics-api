using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LogAnalytics.Infrastructure.Alerts;
using LogAnalytics.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace LogAnalytics.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AlertTests(ApiFactory factory)
{
    [Fact]
    public async Task A_rule_fires_once_its_threshold_is_reached_and_then_cools_down()
    {
        var service = Unique("payments");
        using var admin = await factory.AdminAsync();
        var rule = await CreateRuleAsync(admin, new { name = Unique("Payment errors"), service, threshold = 5, windowMinutes = 5, cooldownMinutes = 30 });

        await SendErrorsAsync(service, 4);
        var before = await EvaluateAsync();
        await SendErrorsAsync(service, 2);
        var fired = await EvaluateAsync();
        var again = await EvaluateAsync();
        var firings = await (await admin.GetAsync("/api/alerts/firings")).ReadAsync<JsonElement>();

        Assert.DoesNotContain(before, f => f.RuleId == rule);
        var firing = Assert.Single(fired, f => f.RuleId == rule);
        Assert.Equal(6, firing.Count);
        Assert.Equal("Payment failed 0", firing.SampleMessage);
        Assert.DoesNotContain(again, f => f.RuleId == rule);
        Assert.Contains(firings.EnumerateArray(), f => f.GetProperty("ruleId").GetGuid() == rule);
    }

    [Fact]
    public async Task A_webhook_gets_the_alert_and_a_failing_one_is_recorded()
    {
        var service = Unique("shipping");
        using var admin = await factory.AdminAsync();
        var ok = await CreateRuleAsync(admin, new { name = Unique("Hook ok"), service, threshold = 1, windowMinutes = 5, channel = "Webhook", webhookUrl = "https://hooks.example.com/ok" });
        var broken = await CreateRuleAsync(admin, new { name = Unique("Hook fail"), service, threshold = 1, windowMinutes = 5, channel = "Webhook", webhookUrl = "https://hooks.example.com/fail" });

        await SendErrorsAsync(service, 1);
        var fired = await EvaluateAsync();

        var delivered = fired.Single(f => f.RuleId == ok);
        var failed = fired.Single(f => f.RuleId == broken);
        Assert.True(delivered.Delivered);
        Assert.False(failed.Delivered);
        Assert.Contains("500", failed.DeliveryError, StringComparison.Ordinal);
        var request = factory.Outbox.Requests.Last(r => r.Uri.AbsoluteUri == "https://hooks.example.com/ok");
        Assert.Contains(service, request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_notification_service_gets_an_email_request_with_an_idempotency_key()
    {
        var service = Unique("checkout");
        using var admin = await factory.AdminAsync();
        var rule = await CreateRuleAsync(admin, new { name = Unique("Email"), service, threshold = 1, windowMinutes = 5, channel = "NotificationService" });

        await SendErrorsAsync(service, 1);
        var firing = (await EvaluateAsync()).Single(f => f.RuleId == rule);

        var request = factory.Outbox.Requests.Last(r => r.Uri.AbsoluteUri == "http://notifications.test/api/notifications");
        Assert.True(firing.Delivered);
        Assert.Equal($"log-alert-{firing.Id}", request.Headers["Idempotency-Key"]);
        Assert.StartsWith("ns_testkey1_", request.Headers["X-Api-Key"], StringComparison.Ordinal);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("log-alert", body.RootElement.GetProperty("template").GetString());
        Assert.Equal(service, body.RootElement.GetProperty("variables").GetProperty("service").GetString());
    }

    [Fact]
    public async Task Rules_are_validated_and_for_admins()
    {
        using var admin = await factory.AdminAsync();
        using var viewer = await factory.ViewerAsync();

        var badHook = await admin.PostAsJsonAsync("/api/alerts/rules", new { name = "Bad hook", threshold = 1, windowMinutes = 5, channel = "Webhook" });
        var byViewer = await viewer.PostAsJsonAsync("/api/alerts/rules", new { name = "Mine", threshold = 1, windowMinutes = 5 });

        Assert.Equal(HttpStatusCode.BadRequest, badHook.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byViewer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/alerts/rules")).StatusCode);
    }

    [Fact]
    public async Task Rules_can_be_changed_and_removed()
    {
        using var admin = await factory.AdminAsync();
        var name = Unique("Change me");
        var id = await CreateRuleAsync(admin, new { name, threshold = 3, windowMinutes = 5 });

        var updated = await admin.PutAsJsonAsync($"/api/alerts/rules/{id}", new { name, threshold = 9, windowMinutes = 10, enabled = false });
        var deleted = await admin.DeleteAsync($"/api/alerts/rules/{id}");
        var missing = await admin.DeleteAsync($"/api/alerts/rules/{id}");

        var body = await updated.ReadAsync<JsonElement>();
        Assert.Equal(9, body.GetProperty("threshold").GetInt32());
        Assert.False(body.GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private async Task<IReadOnlyList<LogAnalytics.Core.Alerts.AlertFiring>> EvaluateAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AlertEvaluation>().RunAsync(CancellationToken.None);
    }

    private async Task SendErrorsAsync(string service, int count)
    {
        var (client, source) = await factory.NewSourceAsync();
        using var _ = client;
        var now = DateTimeOffset.UtcNow;
        (await client.SendClefAsync(string.Join('\n', Enumerable.Range(0, count)
            .Select(i => Logs.Clef(now.AddSeconds(-30 - i), $"Payment failed {i}", "Error", service))))).EnsureSuccessStatusCode();
        await factory.WaitForStoredAsync(source.Id, count);
    }

    private static async Task<Guid> CreateRuleAsync(HttpClient admin, object rule)
    {
        var response = await admin.PostAsJsonAsync("/api/alerts/rules", rule);
        response.EnsureSuccessStatusCode();
        return (await response.ReadAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..24];
}
