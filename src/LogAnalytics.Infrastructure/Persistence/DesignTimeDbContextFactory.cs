using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LogAnalytics.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; generating migrations does not open a connection.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LogsDbContext>
{
    public LogsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LogsDbContext>();
        DependencyInjection.ConfigureDbContext(options, "Host=localhost;Database=log_analytics_design");
        return new LogsDbContext(options.Options);
    }
}
