using System.Text.Json.Nodes;
using LogAnalytics.Core.Events;

namespace LogAnalytics.UnitTests;

public sealed class EventRulesTests
{
    private static readonly DateTimeOffset Oldest = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Newest = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Events_outside_the_kept_window_are_refused()
    {
        var (tooOld, reason) = EventRules.Check(Event(Oldest.AddTicks(-1)), "app", Oldest, Newest);
        var (tooNew, _) = EventRules.Check(Event(Newest), "app", Oldest, Newest);
        var (inside, _) = EventRules.Check(Event(Oldest), "app", Oldest, Newest);

        Assert.Null(tooOld);
        Assert.Null(tooNew);
        Assert.NotNull(inside);
        Assert.Contains("outside", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_text_is_cut_and_oversized_properties_refused()
    {
        var longMessage = Event(Oldest) with { Message = new string('m', 20_000), Exception = new string('x', 40_000) };
        var bigProperties = Event(Oldest) with { Properties = new JsonObject { ["Blob"] = new string('p', 40_000) } };

        var (cut, _) = EventRules.Check(longMessage, "app", Oldest, Newest);
        var (refused, reason) = EventRules.Check(bigProperties, "app", Oldest, Newest);

        Assert.True(cut!.Message.Length <= EventRules.MaxMessageLength + 10);
        Assert.EndsWith("…[cut]", cut.Exception, StringComparison.Ordinal);
        Assert.Null(refused);
        Assert.Contains("32 KB", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_service_falls_back_to_the_source_name_and_control_characters_go()
    {
        var (unnamed, _) = EventRules.Check(Event(Oldest), "Checkout API", Oldest, Newest);
        var (named, _) = EventRules.Check(Event(Oldest) with { Service = "pay\nments\u0007", TraceId = " abc\r\n" }, "x", Oldest, Newest);

        Assert.Equal("Checkout API", unnamed!.Service);
        Assert.Equal("payments", named!.Service);
        Assert.Equal("abc", named.TraceId);
    }

    private static IncomingEvent Event(DateTimeOffset at) =>
        new(at, LogLevel.Information, "hello", null, null, null, null, null, new JsonObject());
}
