using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalytics.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LogAnalytics.IntegrationTests.Support;

public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role, DateTimeOffset CreatedAt);

public sealed record SourceDto(Guid Id, string Name, string KeyPrefix, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

public sealed record CreatedSourceDto(Guid Id, string Name, string KeyPrefix, DateTimeOffset CreatedAt, string ApiKey);

public static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public const string Password = "a-long-test-password";

    public static async Task<HttpClient> AdminAsync(this ApiFactory factory)
    {
        var client = factory.CreateClient();
        await client.SignInAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        return client;
    }

    public static async Task<HttpClient> ViewerAsync(this ApiFactory factory)
    {
        using var admin = await factory.AdminAsync();
        var email = $"viewer-{Guid.NewGuid():N}@example.com";
        (await admin.PostAsJsonAsync("/api/users", new { email, displayName = "Viewer", password = Password })).EnsureSuccessStatusCode();
        var client = factory.CreateClient();
        await client.SignInAsync(email, Password);
        return client;
    }

    public static async Task SignInAsync(this HttpClient client, string email, string password) =>
        (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).EnsureSuccessStatusCode();

    /// <summary>A new source through the admin API and a client that sends with its key.</summary>
    public static async Task<(HttpClient Client, CreatedSourceDto Source)> NewSourceAsync(this ApiFactory factory)
    {
        using var admin = await factory.AdminAsync();
        var response = await admin.PostAsJsonAsync("/api/sources", new { name = $"Source {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        var source = await response.ReadAsync<CreatedSourceDto>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", source.ApiKey);
        return (client, source);
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task<T> DbAsync<T>(this ApiFactory factory, Func<LogsDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LogsDbContext>());
    }
}
