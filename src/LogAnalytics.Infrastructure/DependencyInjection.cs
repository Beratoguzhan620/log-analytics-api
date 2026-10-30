using LogAnalytics.Infrastructure.Ingestion;
using LogAnalytics.Infrastructure.Partitions;
using LogAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LogAnalytics.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLogsInfrastructure(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddDbContext<LogsDbContext>(options => ConfigureDbContext(options, connectionString));
        services.Configure<RetentionOptions>(configuration.GetSection(RetentionOptions.Section));
        services.AddScoped<PartitionMaintenance>();
        services.AddSingleton(new LogsDataSource(connectionString));
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.Section));
        services.AddSingleton<IngestionQueue>();
        services.AddSingleton<LogWriter>();
        services.AddHostedService(provider => provider.GetRequiredService<LogWriter>());
        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
