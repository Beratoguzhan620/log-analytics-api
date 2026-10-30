using LogAnalytics.Infrastructure.Partitions;

namespace LogAnalytics.UnitTests;

public sealed class PartitionNameTests
{
    [Fact]
    public void A_day_has_a_name_and_the_name_gives_the_day_back()
    {
        var day = new DateOnly(2026, 3, 7);

        var name = PartitionMaintenance.NameFor(day);

        Assert.Equal("log_events_y2026m03d07", name);
        Assert.Equal(day, PartitionMaintenance.DayOf(name));
    }

    [Theory]
    [InlineData("log_events")]
    [InlineData("log_events_default")]
    [InlineData("log_events_y2026m13d01")]
    [InlineData("other_y2026m01d01")]
    public void Other_tables_are_not_daily_partitions(string name) => Assert.Null(PartitionMaintenance.DayOf(name));
}
