using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LogAnalytics.Infrastructure.Persistence;

public static class PostgresErrors
{
    public static bool IsUniqueViolation(this DbUpdateException exception, string? constraint = null) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (constraint is null || string.Equals(pg.ConstraintName, constraint, StringComparison.Ordinal));
}
