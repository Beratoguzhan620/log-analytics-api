using Microsoft.AspNetCore.Hosting;
using LogAnalytics.Infrastructure.Alerts;
using LogAnalytics.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LogAnalytics.IntegrationTests;

/// <summary>Boots the real API against a throwaway PostgreSQL container shared by the whole collection.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "admin-password-for-tests";
    public const string SampleKey = "la_sampletk_sample-source-key-used-only-by-the-tests";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _database.GetConnectionString();

    /// <summary>Every request the alert notifier makes, answered with 200 unless the URL says otherwise.</summary>
    public RecordingHandler Outbox { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Logs", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Bootstrap:AdminEmail", AdminEmail);
        builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
        builder.UseSetting("Bootstrap:SampleSourceKey", SampleKey);
        // Every test signs in; the real limit is covered by its own test.
        builder.UseSetting("Auth:LoginAttemptsPerMinute", "100000");
        // Tests run the evaluation themselves, when their events are in place.
        builder.UseSetting("Alerts:EvaluationInterval", "01:00:00");
        builder.UseSetting("Alerts:NotificationService:BaseUrl", "http://notifications.test");
        builder.UseSetting("Alerts:NotificationService:ApiKey", "ns_testkey1_key-for-the-fake-notification-service-0000");
        builder.UseSetting("Alerts:NotificationService:Email", "oncall@example.com");
        builder.ConfigureServices(services => services
            .AddHttpClient(AlertNotifier.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Outbox));
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
