using LogAnalytics.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace LogAnalytics.Infrastructure.Ingestion;

/// <summary>
/// Takes events off the queue and writes them with binary <c>COPY</c>, up to a batch at a time or whatever has
/// arrived within the flush interval. PostgreSQL routes each row to its day's partition. On shutdown the
/// queue is drained before the process exits.
/// </summary>
public sealed partial class LogWriter(
    IngestionQueue queue,
    LogsDataSource dataSource,
    IOptions<IngestionOptions> options,
    TimeProvider clock,
    ILogger<LogWriter> logger) : BackgroundService
{
    private const string Copy = """
        COPY log_events ("timestamp", received_at, source_id, service, level, message, message_template, exception, trace_id, span_id, properties)
        FROM STDIN (FORMAT BINARY)
        """;

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    private long _written;

    public long Written => Interlocked.Read(ref _written);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<LogRecord>(options.Value.BatchSize);
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await FillAsync(batch, stoppingToken);
                await WriteWithRetriesAsync(batch, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: write what is left without the cancelled token.
            while (queue.Reader.TryRead(out var record))
            {
                batch.Add(record);
                if (batch.Count >= options.Value.BatchSize)
                {
                    await WriteWithRetriesAsync(batch, CancellationToken.None);
                }
            }

            await WriteWithRetriesAsync(batch, CancellationToken.None);
        }
    }

    private async Task FillAsync(List<LogRecord> batch, CancellationToken cancellationToken)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(options.Value.FlushInterval);
        try
        {
            while (batch.Count < options.Value.BatchSize)
            {
                if (queue.Reader.TryRead(out var record))
                {
                    batch.Add(record);
                    continue;
                }

                if (!await queue.Reader.WaitToReadAsync(window.Token))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The flush interval is over; write what there is.
        }
    }

    private async Task WriteWithRetriesAsync(List<LogRecord> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await WriteAsync(batch, cancellationToken);
                Interlocked.Add(ref _written, batch.Count);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < RetryDelays.Length)
            {
                LogRetrying(logger, ex, batch.Count, attempt + 1);
                await Task.Delay(RetryDelays[attempt], clock, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Out of retries. The events are lost, loudly; holding them would stop every later batch too.
                LogDropped(logger, ex, batch.Count);
                break;
            }
        }

        queue.MarkTaken(batch.Count);
        batch.Clear();
    }

    private async Task WriteAsync(List<LogRecord> batch, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var importer = await connection.BeginBinaryImportAsync(Copy, cancellationToken);
        foreach (var (sourceId, receivedAt, e) in batch)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(e.Timestamp.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await importer.WriteAsync(receivedAt.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await importer.WriteAsync(sourceId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(e.Service, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync((short)e.Level, NpgsqlDbType.Smallint, cancellationToken);
            await importer.WriteAsync(e.Message, NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(importer, e.MessageTemplate, cancellationToken);
            await WriteNullableAsync(importer, e.Exception, cancellationToken);
            await WriteNullableAsync(importer, e.TraceId, cancellationToken);
            await WriteNullableAsync(importer, e.SpanId, cancellationToken);
            await importer.WriteAsync(e.PropertiesJson, NpgsqlDbType.Jsonb, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static Task WriteNullableAsync(NpgsqlBinaryImporter importer, string? value, CancellationToken cancellationToken) =>
        value is null ? importer.WriteNullAsync(cancellationToken) : importer.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing {Count} events failed (attempt {Attempt}); retrying")]
    private static partial void LogRetrying(ILogger logger, Exception exception, int count, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dropped {Count} events after repeated write failures")]
    private static partial void LogDropped(ILogger logger, Exception exception, int count);
}
