using Microsoft.AspNetCore.Identity;
using LogAnalytics.Core.Users;

namespace LogAnalytics.Api.Features.Auth;

/// <summary>
/// Wraps ASP.NET Core Identity's PBKDF2 password hasher. Unknown emails are checked against a dummy
/// hash so a login for a missing account takes as long as one with a wrong password.
/// </summary>
internal sealed class PasswordVerifier
{
    private static readonly User Dummy = User.Create("dummy@example.invalid", "Dummy", UserRole.Viewer, DateTimeOffset.UnixEpoch);

    private readonly PasswordHasher<User> _hasher = new();
    private readonly string _dummyHash;

    public PasswordVerifier()
    {
        _dummyHash = _hasher.HashPassword(Dummy, Guid.NewGuid().ToString());
    }

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    /// <returns><c>true</c> when the password matches; <paramref name="rehash"/> is set when the stored hash uses outdated parameters.</returns>
    public bool Verify(User? user, string password, out string? rehash)
    {
        rehash = null;
        if (user is null)
        {
            _hasher.VerifyHashedPassword(Dummy, _dummyHash, password);
            return false;
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            rehash = _hasher.HashPassword(user, password);
        }

        return result != PasswordVerificationResult.Failed;
    }
}
