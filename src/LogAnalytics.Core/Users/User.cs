using System.Net.Mail;

namespace LogAnalytics.Core.Users;

/// <summary>A person who reads logs on the dashboard.</summary>
public sealed class User
{
    public const int MaxEmailLength = 254;
    public const int MaxDisplayNameLength = 80;
    public const int MinPasswordLength = 12;

    private User()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Lower-cased and trimmed, so uniqueness does not depend on how it was typed.</summary>
    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public string PasswordHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static User Create(string email, string displayName, UserRole role, DateTimeOffset now)
    {
        var normalized = NormalizeEmail(email);
        if (normalized.Length > MaxEmailLength || !MailAddress.TryCreate(normalized, out var address) || address.Address != normalized)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The email address is not valid.");
        }

        var name = displayName.Trim();
        if (name.Length is < 1 or > MaxDisplayNameLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A display name has 1 to {MaxDisplayNameLength} characters.");
        }

        return new User
        {
            Id = Guid.CreateVersion7(now),
            Email = normalized,
            DisplayName = name,
            Role = role,
            CreatedAt = now,
        };
    }

    public void SetPasswordHash(string hash) => PasswordHash = hash;
}
