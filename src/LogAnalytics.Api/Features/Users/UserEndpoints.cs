using LogAnalytics.Api.Common;
using LogAnalytics.Api.Features.Auth;
using LogAnalytics.Core;
using LogAnalytics.Core.Users;
using LogAnalytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LogAnalytics.Api.Features.Users;

public sealed record CreateUserRequest(string Email, string DisplayName, string Password, UserRole Role = UserRole.Viewer);

internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(AuthSetup.AdminPolicy);
        group.MapGet("/", List).WithSummary("All users (admin)");
        group.MapPost("/", Create).WithSummary("Add a user (admin)").WithDescription("`role` is `Viewer` unless given.");
        return app;
    }

    private static async Task<Ok<List<UserResponse>>> List(LogsDbContext db, CancellationToken ct) =>
        TypedResults.Ok((await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync(ct)).Select(UserResponse.Create).ToList());

    private static async Task<Results<Created<UserResponse>, ValidationProblem>> Create(
        CreateUserRequest request, LogsDbContext db, PasswordVerifier passwords, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Email), "email", "Enter an email address.")
            .Require(!string.IsNullOrWhiteSpace(request.DisplayName), "displayName", "Enter a display name.")
            .Require(request.Password is { Length: >= User.MinPasswordLength }, "password", $"A password has at least {User.MinPasswordLength} characters.")
            .Require(Enum.IsDefined(request.Role), "role", "The role is Viewer or Admin.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var user = User.Create(request.Email, request.DisplayName, request.Role, clock.GetUtcNow());
        user.SetPasswordHash(passwords.Hash(user, request.Password));
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.EmailTaken, "A user with this email already exists.");
        }

        return TypedResults.Created($"/api/users/{user.Id}", UserResponse.Create(user));
    }
}
