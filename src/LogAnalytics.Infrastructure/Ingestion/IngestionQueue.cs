using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace LogAnalytics.Infrastructure.Ingestion;

/// <summary>
/// The bounded buffer between the HTTP requests and the writer. A request's events go in together or not at
/// all, so a sender that is told to retry never ends up with half of its batch stored twice.
/// </summary>
public sealed class IngestionQueue
{
    private readonly Channel<LogRecord> _channel = Channel.CreateUnbounded<LogRecord>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Lock _gate = new();
    private readonly int _capacity;
    private int _count;
    private long _accepted;
    private long _refused;

    public IngestionQueue(IOptions<IngestionOptions> options) => _capacity = options.Value.QueueCapacity;

    public ChannelReader<LogRecord> Reader => _channel.Reader;

    public int Count => Volatile.Read(ref _count);

    public int Capacity => _capacity;

    public long Accepted => Interlocked.Read(ref _accepted);

    /// <summary>Events refused because the queue was full.</summary>
    public long Refused => Interlocked.Read(ref _refused);

    public bool TryEnqueueAll(IReadOnlyCollection<LogRecord> records)
    {
        lock (_gate)
        {
            if (_count + records.Count > _capacity)
            {
                Interlocked.Add(ref _refused, records.Count);
                return false;
            }

            foreach (var record in records)
            {
                _channel.Writer.TryWrite(record);
            }

            _count += records.Count;
        }

        Interlocked.Add(ref _accepted, records.Count);
        return true;
    }

    /// <summary>Called by the writer for every event it has taken out.</summary>
    public void MarkTaken(int count)
    {
        lock (_gate)
        {
            _count -= count;
        }
    }

    public void Complete() => _channel.Writer.TryComplete();
}
