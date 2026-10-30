using LogAnalytics.Core;
using LogAnalytics.Core.Events;
using LogAnalytics.Core.Queries;

namespace LogAnalytics.UnitTests;

public sealed class QueryParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Defaults_are_the_last_hour_and_fifty_events()
    {
        var query = QueryParser.Parse(null, null, null, null, null, null, null, null, null, null, Now);

        Assert.Equal(Now.AddHours(-1), query.From);
        Assert.Equal(Now, query.To);
        Assert.Equal(50, query.Limit);
        Assert.Null(query.MinLevel);
    }

    [Fact]
    public void Every_filter_is_read()
    {
        var cursor = new EventCursor(Now.AddMinutes(-5), 42).Encode();

        var query = QueryParser.Parse(null, null, "24h", "warn", ["checkout", " checkout ", "payments"], "  timeout  ", "abc",
            ["OrderId:SIP-1", "Amount: 12.5"], cursor, 20, Now);

        Assert.Equal(Now.AddDays(-1), query.From);
        Assert.Equal(LogLevel.Warning, query.MinLevel);
        Assert.Equal(new[] { "checkout", "payments" }, query.Services);
        Assert.Equal("timeout", query.Text);
        Assert.Equal(new[] { new PropertyFilter("OrderId", "SIP-1"), new PropertyFilter("Amount", "12.5") }, query.Properties);
        Assert.Equal(new EventCursor(Now.AddMinutes(-5), 42), query.After);
    }

    [Fact]
    public void All_mistakes_are_named_at_once()
    {
        var error = Assert.Throws<DomainException>(() =>
            QueryParser.Parse("yesterday", null, null, "loud", null, null, null, ["no-colon"], "garbage!", 500, Now));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
        Assert.Contains("from", error.Message, StringComparison.Ordinal);
        Assert.Contains("level", error.Message, StringComparison.Ordinal);
        Assert.Contains("prop", error.Message, StringComparison.Ordinal);
        Assert.Contains("cursor", error.Message, StringComparison.Ordinal);
        Assert.Contains("limit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("15m", 15)]
    [InlineData("2h", 120)]
    [InlineData("7d", 10080)]
    public void Ranges_are_minutes_hours_or_days(string range, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), QueryParser.ParseRange(range));

    [Theory]
    [InlineData("0h")]
    [InlineData("1w")]
    [InlineData("90d")]
    [InlineData("h")]
    public void Odd_ranges_are_refused(string range) => Assert.Null(QueryParser.ParseRange(range));

    [Theory]
    [InlineData(15, 30)]
    [InlineData(60, 60)]
    [InlineData(24 * 60, 30 * 60)]
    [InlineData(7 * 24 * 60, 3 * 3600)]
    public void Buckets_give_about_sixty_bars(int windowMinutes, int bucketSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(bucketSeconds), QueryParser.BucketFor(TimeSpan.FromMinutes(windowMinutes)));

    [Fact]
    public void A_cursor_survives_the_round_trip_and_tampering_is_caught()
    {
        var cursor = new EventCursor(Now, 123456789);

        Assert.Equal(cursor, EventCursor.Decode(cursor.Encode()));
        Assert.Null(EventCursor.Decode("bm90LWEtY3Vyc29y"));
        Assert.Null(EventCursor.Decode("%%%"));
    }
}
