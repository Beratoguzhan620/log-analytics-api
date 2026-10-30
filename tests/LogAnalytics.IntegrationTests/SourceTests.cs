using System.Net;
using System.Net.Http.Json;
using LogAnalytics.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace LogAnalytics.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SourceTests(ApiFactory factory)
{
    [Fact]
    public async Task A_new_key_is_shown_once_and_only_its_hash_is_stored()
    {
        using var admin = await factory.AdminAsync();

        var created = await (await admin.PostAsJsonAsync("/api/sources", new { name = $"Checkout {Guid.NewGuid():N}" })).ReadAsync<CreatedSourceDto>();
        var list = await (await admin.GetAsync("/api/sources")).Content.ReadAsStringAsync();
        var stored = await factory.DbAsync(db => db.Sources.AsNoTracking().SingleAsync(s => s.Id == created.Id));

        Assert.StartsWith($"la_{created.KeyPrefix}_", created.ApiKey, StringComparison.Ordinal);
        Assert.DoesNotContain(created.ApiKey, list, StringComparison.Ordinal);
        Assert.Equal(32, stored.KeyHash.Length);
    }

    [Fact]
    public async Task Names_are_unique()
    {
        using var admin = await factory.AdminAsync();
        var name = $"Billing {Guid.NewGuid():N}";

        await admin.PostAsJsonAsync("/api/sources", new { name });
        var again = await admin.PostAsJsonAsync("/api/sources", new { name });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("name_taken", await again.ProblemCodeAsync());
    }

    [Fact]
    public async Task Revoking_twice_is_refused()
    {
        using var admin = await factory.AdminAsync();
        var created = await (await admin.PostAsJsonAsync("/api/sources", new { name = $"Old {Guid.NewGuid():N}" })).ReadAsync<CreatedSourceDto>();

        var first = await admin.PostAsync($"/api/sources/{created.Id}/revoke", null);
        var second = await admin.PostAsync($"/api/sources/{created.Id}/revoke", null);

        Assert.NotNull((await first.ReadAsync<SourceDto>()).RevokedAt);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task The_sample_source_comes_from_configuration()
    {
        using var admin = await factory.AdminAsync();

        var sources = await (await admin.GetAsync("/api/sources")).ReadAsync<List<SourceDto>>();

        Assert.Contains(sources, s => s.Name == "Sample shop" && s.KeyPrefix == "sampletk");
    }
}
