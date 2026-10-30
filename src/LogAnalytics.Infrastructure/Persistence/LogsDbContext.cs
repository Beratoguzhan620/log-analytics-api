using LogAnalytics.Core.Sources;
using LogAnalytics.Core.Users;
using Microsoft.EntityFrameworkCore;

namespace LogAnalytics.Infrastructure.Persistence;

/// <summary>
/// Users, sources and alerts. The partitioned <c>log_events</c> table is created by a hand-written migration
/// and read and written with plain SQL, because EF Core neither creates partitioned tables nor speaks COPY.
/// </summary>
public sealed class LogsDbContext(DbContextOptions<LogsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Source> Sources => Set<Source>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LogsDbContext).Assembly);
}
