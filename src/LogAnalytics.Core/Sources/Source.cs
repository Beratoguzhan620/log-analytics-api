namespace LogAnalytics.Core.Sources;

/// <summary>An application that sends logs, identified by its API key. A key can only send, never read.</summary>
public sealed class Source
{
    public const int MaxNameLength = 100;

    private Source()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string KeyPrefix { get; private set; } = string.Empty;

    public byte[] KeyHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    /// <summary>A source with a fresh key. The returned key is the only copy; the source keeps its hash.</summary>
    public static (Source Source, string Key) Create(string name, DateTimeOffset now)
    {
        var (key, _) = ApiKey.Generate();
        return (WithKey(name, key, now), key);
    }

    /// <summary>A source for a key chosen elsewhere, such as the sample generator's key from configuration.</summary>
    public static Source WithKey(string name, string key, DateTimeOffset now)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 2 or > MaxNameLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A source name has 2 to {MaxNameLength} characters.");
        }

        var prefix = ApiKey.PrefixOf(key)
            ?? throw new DomainException(ErrorCodes.ValidationFailed,
                $"An API key looks like '{ApiKey.Scheme}<8 lowercase letters or digits>_<at least {ApiKey.MinSecretLength} characters>'.");

        return new Source
        {
            Id = Guid.CreateVersion7(now),
            Name = trimmed,
            KeyPrefix = prefix,
            KeyHash = ApiKey.Hash(key),
            CreatedAt = now,
        };
    }

    public bool Accepts(string key) => IsActive && ApiKey.Matches(key, KeyHash);

    public void Revoke(DateTimeOffset now)
    {
        if (!IsActive)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The source is already revoked.");
        }

        RevokedAt = now;
    }
}
