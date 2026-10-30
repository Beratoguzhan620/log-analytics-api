using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;

namespace LogAnalytics.Generator;

/// <summary>
/// Sends a fixed number of events as fast as the API takes them, in CLEF batches from several senders at
/// once, and reports what it measured. A <c>429</c> is respected: the batch is sent again after Retry-After.
/// </summary>
internal static class Load
{
    private static readonly string[] Services = ["checkout", "payments", "catalog", "shipping", "notifications"];

    public static async Task RunAsync(string url, string key, int total, int concurrency, int batchSize)
    {
        using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("X-Api-Key", key);

        var batches = (total + batchSize - 1) / batchSize;
        var next = -1;
        var retries = 0;
        var latencies = new List<double>();
        var gate = new Lock();
        var watch = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async worker =>
        {
            var random = new Random(worker);
            int batch;
            while ((batch = Interlocked.Increment(ref next)) < batches)
            {
                var body = Body(batch, Math.Min(batchSize, total - (batch * batchSize)), random);
                while (true)
                {
                    var started = Stopwatch.GetTimestamp();
                    using var content = new StringContent(body, Encoding.UTF8, "application/vnd.serilog.clef");
                    using var response = await http.PostAsync("/api/events", content);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        Interlocked.Increment(ref retries);
                        await Task.Delay(TimeSpan.FromSeconds(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 1));
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    lock (gate)
                    {
                        latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    }

                    break;
                }
            }
        }));

        watch.Stop();
        latencies.Sort();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{{\"events\":{total},\"seconds\":{watch.Elapsed.TotalSeconds:0.00},\"eventsPerSecond\":{total / watch.Elapsed.TotalSeconds:0}," +
            $"\"requests\":{latencies.Count},\"retriedAfter429\":{retries},\"p50Ms\":{Percentile(latencies, 0.5):0.0},\"p95Ms\":{Percentile(latencies, 0.95):0.0}}}"));
    }

    private static string Body(int batch, int count, Random random)
    {
        var now = DateTimeOffset.UtcNow;
        var builder = new StringBuilder(count * 260);
        for (var i = 0; i < count; i++)
        {
            var at = now.AddSeconds(-random.Next(0, 6 * 3600)).ToString("O", CultureInfo.InvariantCulture);
            var service = Services[random.Next(Services.Length)];
            var error = random.NextDouble() < 0.02;
            var order = (batch * 10_000) + i;
            builder.Append(CultureInfo.InvariantCulture,
                $$"""{"@t":"{{at}}","@mt":"{{(error ? "Order {OrderId} failed after {Elapsed} ms" : "Order {OrderId} handled in {Elapsed} ms")}}","@l":"{{(error ? "Error" : "Information")}}","OrderId":"L-{{order}}","Elapsed":{{random.Next(3, 900)}},"Service":"{{service}}","@tr":"{{Guid.NewGuid():N}}"}""");
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];
}
