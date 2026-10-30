using System.Net;
using System.Text.Json;
using LogAnalytics.IntegrationTests.Support;

namespace LogAnalytics.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class IngestionTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task A_json_array_is_accepted_and_stored()
    {
        var (client, source) = await factory.NewSourceAsync();
        using var _ = client;

        var response = await client.SendJsonAsync(new object[]
        {
            new { timestamp = Now, level = "Error", messageTemplate = "Order {OrderId} failed", properties = new { OrderId = "SIP-1" }, service = "checkout", traceId = "abc123" },
            new { timestamp = Now.AddSeconds(-1), message = "Started" },
        });
        var body = await response.ReadAsync<JsonElement>();
        var stored = await factory.WaitForStoredAsync(source.Id, 2);
        var message = await factory.ScalarAsync<string>(
            "SELECT message FROM log_events WHERE source_id = @id AND level = 4", ("id", source.Id));
        var orderId = await factory.ScalarAsync<string>(
            "SELECT properties->>'OrderId' FROM log_events WHERE source_id = @id AND level = 4", ("id", source.Id));
        var defaultService = await factory.ScalarAsync<string>(
            "SELECT service FROM log_events WHERE source_id = @id AND level = 2", ("id", source.Id));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(2, body.GetProperty("accepted").GetInt32());
        Assert.Equal(2, stored);
        Assert.Equal("Order \"SIP-1\" failed", message);
        Assert.Equal("SIP-1", orderId);
        Assert.Equal(source.Name, defaultService);
    }

    [Fact]
    public async Task Clef_lines_are_accepted_and_bad_lines_reported()
    {
        var (client, source) = await factory.NewSourceAsync();
        using var _ = client;
        var body = string.Join('\n',
            Logs.Clef(Now, "first", service: "catalog"),
            "not json",
            Logs.Clef(Now.AddDays(-40), "too old"),
            Logs.Clef(Now, "second", "Warning"));

        var response = await client.SendClefAsync(body);
        var result = await response.ReadAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(2, result.GetProperty("accepted").GetInt32());
        Assert.Equal(new[] { 1, 2 }, result.GetProperty("rejected").EnumerateArray().Select(r => r.GetProperty("index").GetInt32()));
        Assert.Equal(2, await factory.WaitForStoredAsync(source.Id, 2));
    }

    [Theory]
    [InlineData("/api/events/raw?clef")]
    [InlineData("/ingest/clef")]
    public async Task Seq_clients_can_send_with_their_own_header(string path)
    {
        var (keyed, source) = await factory.NewSourceAsync();
        keyed.Dispose();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Seq-ApiKey", source.ApiKey);

        var response = await client.SendClefAsync(Logs.Clef(Now, "from seq"), path);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("MinimumLevelAccepted", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, await factory.WaitForStoredAsync(source.Id, 1));
    }

    [Fact]
    public async Task A_request_where_nothing_can_be_stored_is_a_bad_request()
    {
        var (client, _) = await factory.NewSourceAsync();
        using var _ = client;

        var response = await client.SendJsonAsync(new object[] { new { message = "no time" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task More_than_a_thousand_events_are_refused()
    {
        var (client, _) = await factory.NewSourceAsync();
        using var _ = client;

        var response = await client.SendJsonAsync(Enumerable.Range(0, 1001).Select(i => new { timestamp = Now, message = $"m{i}" }).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Without_a_valid_key_nothing_is_accepted()
    {
        using var anonymous = factory.CreateClient();
        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Add("X-Api-Key", "la_unknown1_this-key-was-never-issued-by-the-service");
        using var viewer = await factory.ViewerAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendClefAsync(Logs.Clef(Now, "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.SendClefAsync(Logs.Clef(Now, "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.SendClefAsync(Logs.Clef(Now, "x"))).StatusCode);
    }

    [Fact]
    public async Task Ten_thousand_events_in_ten_requests_are_all_written()
    {
        var (client, source) = await factory.NewSourceAsync();
        using var _ = client;

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(batch => client.SendClefAsync(string.Join('\n',
            Enumerable.Range(0, 1000).Select(i => Logs.Clef(Now.AddMilliseconds(-i), $"event {batch}-{i}", i % 50 == 0 ? "Error" : "Information"))))));
        var stored = await factory.WaitForStoredAsync(source.Id, 10_000, seconds: 30);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        Assert.Equal(10_000, stored);
    }

    [Fact]
    public async Task Events_land_in_their_days_partition()
    {
        var (client, source) = await factory.NewSourceAsync();
        using var _ = client;
        var threeDaysAgo = Now.AddDays(-3);

        await client.SendClefAsync(Logs.Clef(threeDaysAgo, "old but kept"));
        await factory.WaitForStoredAsync(source.Id, 1);
        var partition = await factory.ScalarAsync<string>(
            "SELECT tableoid::regclass::text FROM log_events WHERE source_id = @id", ("id", source.Id));

        Assert.Equal(LogAnalytics.Infrastructure.Partitions.PartitionMaintenance.NameFor(DateOnly.FromDateTime(threeDaysAgo.UtcDateTime)), partition);
    }
}
