using LogAnalytics.Core.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace LogAnalytics.Api.Features.Auth;

/// <summary>
/// People sign in with a cookie (the dashboard and the query API); applications send logs with a source key.
/// The two never mix: a source key cannot read, and a session cannot send.
/// </summary>
internal static class AuthSetup
{
    public const string CookieName = "la.session";
    public const string SourceKeyScheme = "SourceKey";
    public const string AdminPolicy = "Admin";
    public const string IngestPolicy = "Ingest";
    public const string AdminRole = nameof(UserRole.Admin);

    public static IServiceCollection AddLogsAuth(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                // Strict keeps the cookie off every request that starts on another site, which is the CSRF defence
                // here together with the API accepting JSON bodies only.
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                // An API answers with status codes; the dashboard decides where to go.
                options.Events.OnRedirectToLogin = context => Status(context, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => Status(context, StatusCodes.Status403Forbidden);
            })
            .AddScheme<AuthenticationSchemeOptions, SourceKeyHandler>(SourceKeyScheme, configureOptions: null);

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(AdminRole))
            .AddPolicy(IngestPolicy, policy => policy
                .AddAuthenticationSchemes(SourceKeyScheme)
                .RequireAuthenticatedUser());

        services.AddSingleton<PasswordVerifier>();
        services.AddScoped<UserSignIn>();
        return services;
    }

    private static Task Status(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
