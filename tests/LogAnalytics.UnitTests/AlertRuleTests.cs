using LogAnalytics.Core;
using LogAnalytics.Core.Alerts;
using LogAnalytics.Core.Events;

namespace LogAnalytics.UnitTests;

public sealed class AlertRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_rule_cools_down_after_it_fires()
    {
        var rule = Rule(cooldownMinutes: 15);

        var firing = rule.Fire(25, "boom", Now);

        Assert.False(rule.IsDue(Now.AddMinutes(10)));
        Assert.True(rule.IsDue(Now.AddMinutes(15)));
        Assert.Equal(Now.AddMinutes(-5), firing.WindowStart);
        Assert.Equal(25, firing.Count);
        Assert.Null(firing.Delivered);
    }

    [Fact]
    public void A_disabled_rule_is_never_due()
    {
        var rule = Rule();
        rule.Update("Errors", null, LogLevel.Error, 10, TimeSpan.FromMinutes(5), TimeSpan.Zero, AlertChannel.None, null, enabled: false);

        Assert.False(rule.IsDue(Now));
    }

    [Theory]
    [InlineData(0, 5, AlertChannel.None, null)]
    [InlineData(10, 0, AlertChannel.None, null)]
    [InlineData(10, 5, AlertChannel.Webhook, null)]
    [InlineData(10, 5, AlertChannel.Webhook, "ftp://example.com/hook")]
    public void Bad_rules_are_refused(int threshold, int windowMinutes, AlertChannel channel, string? url) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() =>
            AlertRule.Create("Errors", null, LogLevel.Error, threshold, TimeSpan.FromMinutes(windowMinutes), TimeSpan.Zero, channel, url, Now)).Code);

    [Fact]
    public void Delivery_errors_are_kept_short()
    {
        var firing = Rule().Fire(1, new string('s', 900), Now);

        firing.MarkFailed(new string('e', 900));

        Assert.False(firing.Delivered);
        Assert.Equal(AlertFiring.MaxTextLength, firing.DeliveryError!.Length);
        Assert.Equal(AlertFiring.MaxTextLength, firing.SampleMessage!.Length);
    }

    private static AlertRule Rule(int cooldownMinutes = 15) =>
        AlertRule.Create("Errors", "payments", LogLevel.Error, 20, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(cooldownMinutes), AlertChannel.None, null, Now);
}
