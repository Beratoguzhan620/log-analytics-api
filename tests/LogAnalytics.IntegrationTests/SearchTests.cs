using System.Net;
using System.Text.Json;
using LogAnalytics.IntegrationTests.Support;

namespace LogAnalytics.IntegrationTests;

public sealed record EventDto(string Key, DateTimeOffset Timestamp, string Level, string Service, string Message, string? MessageTemplate,
    string? Exception, string? TraceId, string? SpanId, JsonElement Properties, string? Source);

public sealed record EventPageDto(List<EventDto> Items, string? NextCursor);

[Collection(ApiCollection.Name)]
public sealed class SearchTests(ApiFactory factory)
{
    [Fact]
    public async Task Filters_narrow_the_results()
    {
        var service = Unique("orders");
        var now = DateTimeOffset.UtcNow;
        await SendAsync(service,
            Logs.Clef(now.AddMinutes(-1), "Payment timed out for order SIP-1", "Error", service, ",\"OrderId\":\"SIP-1\",\"Amount\":12.5"),
            Logs.Clef(now.AddMinutes(-2), "Order SIP-2 placed", "Information", service, ",\"OrderId\":\"SIP-2\""),
            Logs.Clef(now.AddMinutes(-3), "Slow query took 900 ms", "Warning", service),
            Logs.Clef(now.AddHours(-3), "An old error", "Error", service));
        using var viewer = await factory.ViewerAsync();

        var errors = await SearchAsync(viewer, $"service={service}&level=Error");
        var errorsDay = await SearchAsync(viewer, $"service={service}&level=Error&range=24h");
        var text = await SearchAsync(viewer, $"service={service}&q=timed%20out");
        var phrase = await SearchAsync(viewer, $"service={service}&q=%22order%20placed%22");
        var byProperty = await SearchAsync(viewer, $"service={service}&prop=OrderId:SIP-2");
        var byNumber = await SearchAsync(viewer, $"service={service}&prop=Amount:12.5");
        var warningsAndUp = await SearchAsync(viewer, $"service={service}&level=Warning");

        Assert.Equal("Payment timed out for order SIP-1", Assert.Single(errors.Items).Message);
        Assert.Equal(2, errorsDay.Items.Count);
        Assert.Single(text.Items);
        Assert.Empty(phrase.Items);
        Assert.Equal("Order SIP-2 placed", Assert.Single(byProperty.Items).Message);
        Assert.Single(byNumber.Items);
        Assert.Equal(2, warningsAndUp.Items.Count);
        Assert.Equal("SIP-1", errors.Items[0].Properties.GetProperty("OrderId").GetString());
    }

    [Fact]
    public async Task Pages_follow_each_other_without_gaps_or_repeats()
    {
        var service = Unique("paging");
        var now = DateTimeOffset.UtcNow;
        // Several events share a timestamp, so the id has to break the tie.
        await SendAsync(service, Enumerable.Range(0, 25).Select(i => Logs.Clef(now.AddSeconds(-(i / 3)), $"event {i}", service: service)).ToArray());
        using var viewer = await factory.ViewerAsync();

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await SearchAsync(viewer, $"service={service}&limit=10" + (cursor is null ? "" : $"&cursor={cursor}"));
            seen.AddRange(page.Items.Select(e => e.Message));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(25, seen.Distinct().Count());
        Assert.Equal(25, seen.Count);
    }

    [Fact]
    public async Task One_event_and_its_trace_can_be_opened()
    {
        var service = Unique("trace");
        var now = DateTimeOffset.UtcNow;
        var trace = Guid.NewGuid().ToString("N");
        await SendAsync(service,
            Logs.Clef(now.AddSeconds(-3), "checkout started", service: service, extra: $",\"@tr\":\"{trace}\""),
            Logs.Clef(now.AddSeconds(-2), "payment charged", service: Unique("payments"), extra: $",\"@tr\":\"{trace}\""),
            Logs.Clef(now.AddSeconds(-1), "unrelated", service: service));
        using var viewer = await factory.ViewerAsync();

        var first = (await SearchAsync(viewer, $"traceId={trace}&level=Information")).Items[^1];
        var single = await (await viewer.GetAsync($"/api/events/{first.Key}")).ReadAsync<EventDto>();
        var traced = await (await viewer.GetAsync($"/api/traces/{trace}")).ReadAsync<List<EventDto>>();

        Assert.Equal("checkout started", single.Message);
        Assert.Equal(new[] { "checkout started", "payment charged" }, traced.Select(e => e.Message));
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/events/not-a-key")).StatusCode);
    }

    [Fact]
    public async Task Statistics_count_per_level_service_and_kind_of_error()
    {
        var service = Unique("stats");
        var now = DateTimeOffset.UtcNow;
        var lines = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            lines.Add($$"""{"@t":"{{now.AddMinutes(-i):O}}","@mt":"Order {OrderId} failed","@l":"Error","OrderId":{{i}},"Service":"{{service}}","@x":"System.TimeoutException: gateway\n   at Pay()"}""");
        }

        lines.Add(Logs.Clef(now.AddMinutes(-1), "Card declined", "Error", service));
        lines.Add(Logs.Clef(now.AddMinutes(-1), "Slow", "Warning", service));
        lines.AddRange(Enumerable.Range(0, 12).Select(i => Logs.Clef(now.AddMinutes(-i), $"ok {i}", service: service)));
        await SendAsync(service, lines.ToArray());
        using var viewer = await factory.ViewerAsync();

        var top = await (await viewer.GetAsync($"/api/stats/top-errors?service={service}")).ReadAsync<JsonElement>();
        var volume = await (await viewer.GetAsync($"/api/stats/volume?service={service}&range=1h")).ReadAsync<JsonElement>();
        var overview = await (await viewer.GetAsync("/api/stats/overview?range=1h")).ReadAsync<JsonElement>();

        var biggest = top[0];
        Assert.Equal("Order {OrderId} failed", biggest.GetProperty("template").GetString());
        Assert.Equal(6, biggest.GetProperty("count").GetInt32());
        Assert.Equal("System.TimeoutException: gateway", biggest.GetProperty("sampleException").GetString());
        Assert.Equal(2, top.GetArrayLength());

        Assert.Equal("00:01:00", volume.GetProperty("bucket").GetString());
        var points = volume.GetProperty("points").EnumerateArray().ToList();
        Assert.InRange(points.Count, 60, 61);
        Assert.Equal(20, points.Sum(p => p.GetProperty("total").GetInt32()));
        Assert.Equal(7, points.Sum(p => p.GetProperty("counts").GetProperty("Error").GetInt32()));

        var mine = overview.GetProperty("services").EnumerateArray().Single(s => s.GetProperty("service").GetString() == service);
        Assert.Equal(20, mine.GetProperty("total").GetInt32());
        Assert.Equal(7, mine.GetProperty("errors").GetInt32());
        Assert.Equal(0.35, mine.GetProperty("errorRate").GetDouble());
    }

    [Fact]
    public async Task Bad_queries_name_their_mistakes_and_people_must_sign_in()
    {
        using var viewer = await factory.ViewerAsync();
        using var anonymous = factory.CreateClient();

        var bad = await viewer.GetAsync("/api/events?range=1w&level=loud");

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("validation_failed", await bad.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/events")).StatusCode);
    }

    private async Task SendAsync(string service, params string[] lines)
    {
        var (client, source) = await factory.NewSourceAsync();
        using var _ = client;
        (await client.SendClefAsync(string.Join('\n', lines))).EnsureSuccessStatusCode();
        await factory.WaitForStoredAsync(source.Id, lines.Length);
    }

    private static async Task<EventPageDto> SearchAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/events?{query}");
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<EventPageDto>();
    }

    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..20];
}
