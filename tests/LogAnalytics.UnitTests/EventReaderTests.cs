using LogAnalytics.Core;
using LogAnalytics.Core.Events;

namespace LogAnalytics.UnitTests;

public sealed class EventReaderTests
{
    [Fact]
    public void Clef_lines_become_events_with_their_properties()
    {
        const string body = """
            {"@t":"2026-10-30T10:15:00.123Z","@mt":"Order {OrderId} paid in {Elapsed} ms","@l":"Warning","OrderId":"SIP-1","Elapsed":42,"Service":"payments","@tr":"4bf92f3577b34da6a3ce929d0e0e4736","@sp":"00f067aa0ba902b7"}

            {"@t":"2026-10-30T10:15:01Z","@m":"Plain message","@x":"System.Exception: boom","@@t":"kept"}
            """;

        var (events, rejected) = EventReader.ReadClef(body);

        Assert.Empty(rejected);
        Assert.Equal(2, events.Count);
        var first = events[0].Event;
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 10, 15, 0, 123, TimeSpan.Zero), first.Timestamp);
        Assert.Equal(LogLevel.Warning, first.Level);
        Assert.Equal("Order \"SIP-1\" paid in 42 ms", first.Message);
        Assert.Equal("Order {OrderId} paid in {Elapsed} ms", first.MessageTemplate);
        Assert.Equal("payments", first.Service);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", first.TraceId);
        Assert.Equal(42, (int)first.Properties["Elapsed"]!);
        var second = events[1].Event;
        Assert.Equal(LogLevel.Information, second.Level);
        Assert.Equal("System.Exception: boom", second.Exception);
        Assert.Equal("kept", (string)second.Properties["@t"]!);
        Assert.Equal(1, events[1].Index);
    }

    [Fact]
    public void Bad_clef_lines_are_reported_by_position_and_the_rest_kept()
    {
        const string body = """
            {"@t":"2026-10-30T10:15:00Z","@m":"ok"}
            not json
            {"@m":"no time"}
            {"@t":"2026-10-30T10:15:00Z"}
            {"@t":"2026-10-30T10:15:00Z","@m":"x","@l":"Loud"}
            [1,2]
            """;

        var (events, rejected) = EventReader.ReadClef(body);

        Assert.Single(events);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rejected.Select(r => r.Index));
        Assert.Contains("@t", rejected[1].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_arrays_use_readable_field_names()
    {
        const string body = """
            [
              {"timestamp":"2026-10-30T10:15:00+03:00","level":"Error","messageTemplate":"Card {Last4} declined","properties":{"Last4":"4242"},"service":"payments","exception":"x"},
              {"timestamp":"2026-10-30T10:15:00Z","message":"no level means Information"},
              {"level":"Error","message":"no time"},
              "not an object"
            ]
            """;

        var (events, rejected) = EventReader.ReadJsonArray(body);

        Assert.Equal(2, events.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 7, 15, 0, TimeSpan.Zero), events[0].Event.Timestamp);
        Assert.Equal("Card \"4242\" declined", events[0].Event.Message);
        Assert.Equal(LogLevel.Information, events[1].Event.Level);
        Assert.Equal(new[] { 2, 3 }, rejected.Select(r => r.Index));
    }

    [Fact]
    public void A_body_that_is_not_an_array_is_refused() =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => EventReader.ReadJsonArray("{\"message\":\"x\"}")).Code);

    [Theory]
    [InlineData("Hello {Name}", "Hello \"Ayşe\"")]
    [InlineData("Hello {Name:l}", "Hello Ayşe")]
    [InlineData("Took {Elapsed:0.0} ms", "Took 12.5 ms")]
    [InlineData("Order {@Order}", "Order {\"Id\":1}")]
    [InlineData("Missing {Nope}", "Missing {Nope}")]
    [InlineData("Braces {{literal}}", "Braces {literal}")]
    [InlineData("Unclosed {Name", "Unclosed {Name")]
    public void Templates_render_like_serilog(string template, string expected)
    {
        var properties = new System.Text.Json.Nodes.JsonObject
        {
            ["Name"] = "Ayşe",
            ["Elapsed"] = 12.5,
            ["Order"] = new System.Text.Json.Nodes.JsonObject { ["Id"] = 1 },
        };

        Assert.Equal(expected, MessageTemplate.Render(template, properties));
    }

    [Theory]
    [InlineData(null, LogLevel.Information)]
    [InlineData("WRN", LogLevel.Warning)]
    [InlineData("critical", LogLevel.Fatal)]
    [InlineData("Trace", LogLevel.Verbose)]
    public void Levels_accept_serilog_and_microsoft_names(string? name, LogLevel expected) => Assert.Equal(expected, Levels.Parse(name));
}
