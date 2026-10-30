using Npgsql;

namespace LogAnalytics.Infrastructure.Persistence;

/// <summary>The Npgsql data source for what EF Core does not do: <c>COPY</c> and the log queries.</summary>
public sealed class LogsDataSource(string connectionString) : IAsyncDisposable
{
    public NpgsqlDataSource Value { get; } = NpgsqlDataSource.Create(connectionString);

    public ValueTask DisposeAsync() => Value.DisposeAsync();
}
