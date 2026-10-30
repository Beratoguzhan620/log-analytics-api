using LogAnalytics.Infrastructure.Partitions;
using LogAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace LogAnalytics.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PartitionTests(ApiFactory factory)
{
    [Fact]
    public async Task Startup_creates_a_partition_for_every_day_in_the_window()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<PartitionMaintenance>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var partitions = await maintenance.ListAsync(CancellationToken.None);

        Assert.Contains(PartitionMaintenance.NameFor(today), partitions);
        Assert.Contains(PartitionMaintenance.NameFor(today.AddDays(2)), partitions);
        Assert.Contains(PartitionMaintenance.NameFor(today.AddDays(-13)), partitions);
        Assert.True(partitions.Count >= 16);
    }

    [Fact]
    public async Task Days_that_fall_out_of_retention_are_dropped_and_new_ones_created()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LogsDbContext>();
        var later = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(30));
        var maintenance = new PartitionMaintenance(db, Options.Create(new RetentionOptions { Days = 14, DaysAhead = 2 }), later);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var changes = await maintenance.RunAsync(CancellationToken.None);
        var partitions = await maintenance.ListAsync(CancellationToken.None);

        Assert.Contains(PartitionMaintenance.NameFor(today), changes.Dropped);
        Assert.DoesNotContain(PartitionMaintenance.NameFor(today), partitions);
        Assert.Contains(PartitionMaintenance.NameFor(today.AddDays(32)), partitions);
        Assert.All(partitions, name => Assert.True(PartitionMaintenance.DayOf(name) >= today.AddDays(30 - 13)));

        // Put today's window back for the other tests.
        var restore = new PartitionMaintenance(db, Options.Create(new RetentionOptions { Days = 14, DaysAhead = 2 }), TimeProvider.System);
        await restore.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Running_twice_changes_nothing()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<PartitionMaintenance>();

        await maintenance.RunAsync(CancellationToken.None);
        var second = await maintenance.RunAsync(CancellationToken.None);

        Assert.Empty(second.Created);
        Assert.Empty(second.Dropped);
    }
}
