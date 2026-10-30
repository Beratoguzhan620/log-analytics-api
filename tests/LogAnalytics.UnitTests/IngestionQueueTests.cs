using LogAnalytics.Core.Events;
using LogAnalytics.Infrastructure.Ingestion;
using Microsoft.Extensions.Options;

namespace LogAnalytics.UnitTests;

public sealed class IngestionQueueTests
{
    [Fact]
    public void A_batch_goes_in_whole_or_not_at_all()
    {
        var queue = new IngestionQueue(Options.Create(new IngestionOptions { QueueCapacity = 5 }));

        Assert.True(queue.TryEnqueueAll(Records(3)));
        Assert.False(queue.TryEnqueueAll(Records(3)));
        Assert.True(queue.TryEnqueueAll(Records(2)));

        Assert.Equal(5, queue.Count);
        Assert.Equal(5, queue.Accepted);
        Assert.Equal(3, queue.Refused);
    }

    [Fact]
    public void Taking_events_out_makes_room_again()
    {
        var queue = new IngestionQueue(Options.Create(new IngestionOptions { QueueCapacity = 2 }));
        queue.TryEnqueueAll(Records(2));

        Assert.True(queue.Reader.TryRead(out _));
        queue.MarkTaken(1);

        Assert.True(queue.TryEnqueueAll(Records(1)));
    }

    private static List<LogRecord> Records(int count) => Enumerable.Range(0, count)
        .Select(_ => new LogRecord(Guid.NewGuid(), DateTimeOffset.UtcNow,
            new StoredEvent(DateTimeOffset.UtcNow, LogLevel.Information, "svc", "m", null, null, null, null, "{}")))
        .ToList();
}
