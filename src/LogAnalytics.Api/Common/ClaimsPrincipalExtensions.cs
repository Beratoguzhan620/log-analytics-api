using System.Security.Claims;

namespace LogAnalytics.Api.Common;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in user. Endpoints that call this require sign-in, so the claim is always present.</summary>
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The principal has no user id claim."));
}
