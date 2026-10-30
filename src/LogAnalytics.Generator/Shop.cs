using System.Diagnostics;
using Serilog;
using Serilog.Events;

namespace LogAnalytics.Generator;

/// <summary>
/// Five services handling orders. Every order is one trace: checkout, payment, shipping and the email share
/// a trace id. Every so often the payment provider has an outage and errors pile up for a couple of minutes.
/// </summary>
internal sealed class Shop(ILogger logger, Random random)
{
    private static readonly string[] Skus = ["SKU-1001", "SKU-1002", "SKU-2040", "SKU-3300", "SKU-4120", "SKU-5009"];
    private static readonly string[] Categories = ["shoes", "jackets", "bags", "accessories"];
    private static readonly string[] Providers = ["iyzico", "PayTR"];
    private static readonly string[] Carriers = ["Yurtiçi Kargo", "Aras Kargo", "MNG Kargo"];
    private static readonly string[] Templates = ["order-shipped", "order-confirmed", "password-changed"];

    private int _orderNo = 40_000;

    /// <summary>Events in the past, denser in the day than at night, with three incidents.</summary>
    public int Backfill(DateTimeOffset from, DateTimeOffset to)
    {
        var total = to - from;
        var incidents = new[] { 0.22, 0.58, 0.86 }.Select(f => from + total * f).ToArray();
        var count = 0;
        for (var at = from; at < to;)
        {
            var hour = at.ToOffset(TimeSpan.FromHours(3)).Hour;
            var busy = hour is >= 9 and <= 23 ? 1.0 : 0.35;
            var outage = incidents.Any(i => at >= i && at < i + TimeSpan.FromMinutes(6));
            count += Order(at, outage);
            count += Background(at);
            at += TimeSpan.FromSeconds(random.NextDouble() * 12 / busy);
        }

        return count;
    }

    public async Task RunLiveAsync(double eventsPerSecond, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        // About six events per order and background tick.
        var pause = TimeSpan.FromSeconds(6 / Math.Max(eventsPerSecond, 0.1));
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            // A two-minute outage every twenty minutes, starting five minutes in.
            var minute = (now - started).TotalMinutes;
            var outage = minute > 5 && minute % 20 < 2;
            Order(now, outage);
            Background(now);
            try
            {
                await Task.Delay(pause * (0.5 + random.NextDouble()), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private int Order(DateTimeOffset at, bool outage)
    {
        var trace = ActivityTraceId.CreateRandom();
        var orderId = $"SIP-{Interlocked.Increment(ref _orderNo)}";
        var customerId = $"C-{random.Next(1000, 9999)}";
        var amount = Math.Round(random.NextDouble() * 2400 + 150, 2);
        var items = random.Next(1, 5);
        var provider = Pick(Providers);
        var count = 0;

        count += Write("checkout", at, LogEventLevel.Information, trace, null,
            "Order {OrderId} placed by customer {CustomerId} for {Amount} TRY ({ItemCount} items)", orderId, customerId, amount, items);

        var paid = at.AddMilliseconds(random.Next(80, 400));
        if (outage && random.NextDouble() < 0.85)
        {
            var elapsed = random.Next(5000, 10000);
            count += Write("payments", paid, LogEventLevel.Error, trace,
                new TimeoutException($"The request to {provider} timed out after {elapsed} ms."),
                "Payment provider {Provider} did not answer for order {OrderId} after {Elapsed} ms", provider, orderId, elapsed);
            count += Write("checkout", paid.AddMilliseconds(20), LogEventLevel.Error, trace, null,
                "Order {OrderId} failed: the payment could not be completed", orderId);
            return count;
        }

        if (random.NextDouble() < 0.06)
        {
            count += Write("payments", paid, LogEventLevel.Warning, trace, null,
                "Payment for order {OrderId} retried after {Elapsed} ms", orderId, random.Next(900, 2500));
        }

        if (random.NextDouble() < 0.01)
        {
            count += Write("payments", paid, LogEventLevel.Error, trace,
                new InvalidOperationException("Card declined: insufficient funds."),
                "Payment provider {Provider} returned {StatusCode} for order {OrderId}", provider, 402, orderId);
            return count;
        }

        count += Write("payments", paid, LogEventLevel.Information, trace, null,
            "Charged {Amount} TRY for order {OrderId} with {Provider}", amount, orderId, provider);

        var shipped = paid.AddMilliseconds(random.Next(200, 900));
        var carrier = Pick(Carriers);
        if (random.NextDouble() < 0.04)
        {
            count += Write("shipping", shipped, LogEventLevel.Warning, trace, null,
                "Carrier {Carrier} rate limit hit, retrying in {Delay} s", carrier, random.Next(2, 10));
        }

        count += Write("shipping", shipped, LogEventLevel.Information, trace, null,
            "Shipment {ShipmentId} for order {OrderId} handed to {Carrier}", $"SH-{random.Next(100000, 999999)}", orderId, carrier);

        var template = Templates[random.NextDouble() < 0.7 ? 0 : 1];
        if (random.NextDouble() < 0.015)
        {
            count += Write("notifications", shipped.AddMilliseconds(150), LogEventLevel.Error, trace,
                new IOException("451 4.7.1 Try again later"),
                "Email {Template} for customer {CustomerId} could not be sent", template, customerId);
        }
        else
        {
            count += Write("notifications", shipped.AddMilliseconds(150), LogEventLevel.Information, trace, null,
                "Email {Template} sent to customer {CustomerId}", template, customerId);
        }

        return count;
    }

    private int Background(DateTimeOffset at)
    {
        var count = 0;
        var trace = ActivityTraceId.CreateRandom();
        var elapsed = random.Next(4, 120);
        count += Write("catalog", at, LogEventLevel.Information, trace, null,
            "GET /products/{Sku} answered {StatusCode} in {Elapsed} ms", Pick(Skus), 200, elapsed);
        if (random.NextDouble() < 0.05)
        {
            count += Write("catalog", at, LogEventLevel.Warning, trace, null,
                "Slow query for category {Category} took {Elapsed} ms", Pick(Categories), random.Next(800, 3000));
        }

        if (random.NextDouble() < 0.2)
        {
            count += Write("checkout", at, LogEventLevel.Debug, trace, null,
                "Basket {BasketId} expired after {Minutes} minutes", $"B-{random.Next(10000, 99999)}", 30);
        }

        return count;
    }

    private int Write(string service, DateTimeOffset at, LogEventLevel level, ActivityTraceId trace, Exception? exception, string template, params object?[] values)
    {
        if (!logger.BindMessageTemplate(template, values, out var parsed, out var bound))
        {
            return 0;
        }

        var properties = bound.ToList();
        properties.Add(new LogEventProperty("Service", new ScalarValue(service)));
        logger.Write(new LogEvent(at, level, exception, parsed, properties, trace, ActivitySpanId.CreateRandom()));
        return 1;
    }

    private T Pick<T>(T[] items) => items[random.Next(items.Length)];
}
