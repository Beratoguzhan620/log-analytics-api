using LogAnalytics.Api.Features.Auth;
using LogAnalytics.Core.Sources;
using LogAnalytics.Core.Users;
using LogAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogAnalytics.Api.Features.Users;

/// <summary>
/// What a fresh installation needs from configuration: the first admin (when there are no users yet) and,
/// optionally, a source with a known key for the sample generator. Secrets are read, never written anywhere.
/// </summary>
internal static partial class Bootstrap
{
    public const string SampleSourceName = "Sample shop";

    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var db = scope.ServiceProvider.GetRequiredService<LogsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Bootstrap));

        var email = configuration["Bootstrap:AdminEmail"];
        var password = configuration["Bootstrap:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrEmpty(password) && !await db.Users.AnyAsync(cancellationToken))
        {
            if (password.Length < User.MinPasswordLength)
            {
                throw new InvalidOperationException($"Bootstrap:AdminPassword needs at least {User.MinPasswordLength} characters.");
            }

            var admin = User.Create(email, "Administrator", UserRole.Admin, now);
            admin.SetPasswordHash(scope.ServiceProvider.GetRequiredService<PasswordVerifier>().Hash(admin, password));
            db.Users.Add(admin);
            await db.SaveChangesAsync(cancellationToken);
            LogAdminCreated(logger);
        }

        var sampleKey = configuration["Bootstrap:SampleSourceKey"];
        if (!string.IsNullOrWhiteSpace(sampleKey) && ApiKey.PrefixOf(sampleKey) is { } prefix
            && !await db.Sources.AnyAsync(s => s.KeyPrefix == prefix || s.Name == SampleSourceName, cancellationToken))
        {
            db.Sources.Add(Source.WithKey(SampleSourceName, sampleKey, now));
            await db.SaveChangesAsync(cancellationToken);
            LogSampleSourceCreated(logger, prefix);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the first admin from configuration")]
    private static partial void LogAdminCreated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the sample source (key prefix {KeyPrefix})")]
    private static partial void LogSampleSourceCreated(ILogger logger, string keyPrefix);
}
