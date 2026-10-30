using System.Diagnostics;
using LogAnalytics.Generator;
using Serilog;

// Logs like a small shop's services would, through the ordinary Seq sink, so the stack has data from the start.
//   LOGS_URL            where Log Analytics listens (default http://localhost:8080)
//   LOGS_API_KEY        a source key
//   EVENTS_PER_SECOND   live traffic (default 8)
//   BACKFILL_HOURS      history to create first, with a few incidents in it (default 24, 0 for none)
var url = Environment.GetEnvironmentVariable("LOGS_URL") ?? "http://localhost:8080";
var key = Environment.GetEnvironmentVariable("LOGS_API_KEY")
    ?? throw new InvalidOperationException("Set LOGS_API_KEY to a source key.");
var perSecond = double.Parse(Environment.GetEnvironmentVariable("EVENTS_PER_SECOND") ?? "8", System.Globalization.CultureInfo.InvariantCulture);
var backfillHours = int.Parse(Environment.GetEnvironmentVariable("BACKFILL_HOURS") ?? "24", System.Globalization.CultureInfo.InvariantCulture);

await WaitForApiAsync(url);

await using var logger = new LoggerConfiguration()
    .MinimumLevel.Verbose()
    .WriteTo.Seq(url, apiKey: key, batchPostingLimit: 1000, period: TimeSpan.FromSeconds(1), queueSizeLimit: 500_000)
    .CreateLogger();

var shop = new Shop(logger, new Random(20261030));
if (backfillHours > 0)
{
    var started = Stopwatch.StartNew();
    var count = shop.Backfill(DateTimeOffset.UtcNow.AddHours(-backfillHours), DateTimeOffset.UtcNow);
    Console.WriteLine($"Backfilled {count} events for the last {backfillHours} hours in {started.Elapsed.TotalSeconds:0.0} s.");
}

Console.WriteLine($"Sending about {perSecond} events a second to {url}.");
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
await shop.RunLiveAsync(perSecond, stop.Token);

static async Task WaitForApiAsync(string url)
{
    using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(3) };
    for (var attempt = 1; attempt <= 60; attempt++)
    {
        try
        {
            if ((await http.GetAsync("/health/ready")).IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Not up yet.
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    throw new InvalidOperationException($"{url} did not become ready.");
}
