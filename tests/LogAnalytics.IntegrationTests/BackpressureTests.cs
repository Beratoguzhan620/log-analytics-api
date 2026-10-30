using System.Net;
using System.Net.Http.Json;
using LogAnalytics.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;

namespace LogAnalytics.IntegrationTests;

/// <summary>The same API with a tiny queue and a tight rate limit, to see both kinds of push-back.</summary>
public sealed class SmallLimitsFactory : IAsyncLifetime
{
    public ApiFactory Inner { get; } = new LimitedApiFactory();

    public Task InitializeAsync() => Inner.InitializeAsync();

    public Task DisposeAsync() => ((IAsyncLifetime)Inner).DisposeAsync();

    private sealed class LimitedApiFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Ingestion:QueueCapacity", "10");
            builder.UseSetting("Ingestion:RequestsPerSecondPerSource", "2");
        }
    }
}

public sealed class BackpressureTests(SmallLimitsFactory limits) : IClassFixture<SmallLimitsFactory>
{
    [Fact]
    public async Task A_batch_bigger_than_the_free_space_gets_429_with_retry_after()
    {
        var (client, _) = await limits.Inner.NewSourceAsync();
        using var _ = client;

        var response = await client.SendClefAsync(string.Join('\n', Enumerable.Range(0, 11).Select(i => Logs.Clef(DateTimeOffset.UtcNow, $"e{i}"))));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("1", response.Headers.RetryAfter!.ToString());
    }

    [Fact]
    public async Task A_source_that_sends_too_often_is_slowed_down_but_others_are_not()
    {
        var (noisy, _) = await limits.Inner.NewSourceAsync();
        var (quiet, _) = await limits.Inner.NewSourceAsync();
        using var _ = noisy;
        using var __ = quiet;

        var burst = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            burst.Add((await noisy.SendClefAsync(Logs.Clef(DateTimeOffset.UtcNow, "noise"))).StatusCode);
        }

        var other = await quiet.SendClefAsync(Logs.Clef(DateTimeOffset.UtcNow, "calm"));

        Assert.Contains(HttpStatusCode.TooManyRequests, burst);
        Assert.Equal(HttpStatusCode.Accepted, other.StatusCode);
    }
}

/// <summary>Sign-in attempts are limited per address.</summary>
public sealed class LoginLimitTests(LoginLimitTests.Factory factory) : IClassFixture<LoginLimitTests.Factory>
{
    [Fact]
    public async Task Guessing_passwords_is_slowed_down()
    {
        using var client = factory.Inner.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = $"guess-number-{i}" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    public sealed class Factory : IAsyncLifetime
    {
        public ApiFactory Inner { get; } = new Limited();

        public Task InitializeAsync() => Inner.InitializeAsync();

        public Task DisposeAsync() => ((IAsyncLifetime)Inner).DisposeAsync();

        private sealed class Limited : ApiFactory
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseSetting("Auth:LoginAttemptsPerMinute", "3");
            }
        }
    }
}
