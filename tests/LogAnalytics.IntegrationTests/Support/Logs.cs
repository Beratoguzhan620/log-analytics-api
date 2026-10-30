using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using LogAnalytics.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LogAnalytics.IntegrationTests.Support;

public static class Logs
{
    public static Task<HttpResponseMessage> SendJsonAsync(this HttpClient client, object events) =>
        client.PostAsJsonAsync("/api/events", events);

    public static Task<HttpResponseMessage> SendClefAsync(this HttpClient client, string body, string path = "/api/events")
    {
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.serilog.clef");
        return client.PostAsync(path, content);
    }

    public static string Clef(DateTimeOffset at, string message, string level = "Information", string? service = null, string extra = "") =>
        $$"""{"@t":"{{at:O}}","@m":"{{message}}","@l":"{{level}}"{{(service is null ? "" : $",\"Service\":\"{service}\"")}}{{extra}}}""";

    /// <summary>Waits until the writer has stored <paramref name="expected"/> events for a source.</summary>
    public static async Task<long> WaitForStoredAsync(this ApiFactory factory, Guid sourceId, long expected, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        long count;
        do
        {
            count = await factory.ScalarAsync<long>("SELECT count(*) FROM log_events WHERE source_id = @id", ("id", sourceId));
            if (count >= expected)
            {
                return count;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        return count;
    }

    public static async Task<T> ScalarAsync<T>(this ApiFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        var dataSource = factory.Services.GetRequiredService<LogsDataSource>().Value;
        await using var command = dataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, value));
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }
}
