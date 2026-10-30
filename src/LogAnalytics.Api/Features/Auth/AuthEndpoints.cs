using System.Security.Claims;
using LogAnalytics.Api.Common;
using LogAnalytics.Api.Logging;
using LogAnalytics.Core;
using LogAnalytics.Core.Users;
using LogAnalytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LogAnalytics.Api.Features.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record UserResponse(Guid Id, string Email, string DisplayName, UserRole Role, DateTimeOffset CreatedAt)
{
    public static UserResponse Create(User user) => new(user.Id, user.Email, user.DisplayName, user.Role, user.CreatedAt);
}

internal static class AuthEndpoints
{
    public const string LoginRateLimit = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Sign-in");
        group.MapPost("/auth/login", Login)
            .RequireRateLimiting(LoginRateLimit)
            .WithSummary("Sign in with email and password")
            .WithDescription("Sets an HTTP-only session cookie that the dashboard and the query endpoints use.");
        group.MapPost("/auth/logout", Logout).WithSummary("Sign out");
        group.MapGet("/me", Me).RequireAuthorization().WithSummary("The signed-in user");
        return app;
    }

    private static async Task<Results<Ok<UserResponse>, ValidationProblem>> Login(
        LoginRequest request, HttpContext context, UserSignIn signIn, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Email), "email", "Enter your email.")
            .Require(!string.IsNullOrEmpty(request.Password), "password", "Enter your password.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var user = await signIn.VerifyAsync(request.Email, request.Password, ct);
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return TypedResults.Ok(UserResponse.Create(user));
    }

    private static async Task<NoContent> Logout(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<UserResponse>> Me(ClaimsPrincipal principal, LogsDbContext db, CancellationToken ct)
    {
        var id = principal.UserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "The user no longer exists.");
        return TypedResults.Ok(UserResponse.Create(user));
    }
}

/// <summary>Checks credentials. Every failure looks the same and takes as long, whether or not the email exists.</summary>
internal sealed partial class UserSignIn(LogsDbContext db, PasswordVerifier passwords, ILogger<UserSignIn> logger)
{
    public async Task<User> VerifyAsync(string email, string password, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalized, ct);

        if (!passwords.Verify(user, password, out var rehash) || user is null)
        {
            LogFailed(logger, Masking.Email(normalized));
            throw new DomainException(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");
        }

        if (rehash is not null)
        {
            user.SetPasswordHash(rehash);
            await db.SaveChangesAsync(ct);
        }

        return user;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed sign-in for {MaskedEmail}")]
    private static partial void LogFailed(ILogger logger, string maskedEmail);
}
