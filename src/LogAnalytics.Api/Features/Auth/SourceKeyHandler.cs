using System.Security.Claims;
using System.Text.Encodings.Web;
using LogAnalytics.Core.Sources;
using LogAnalytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LogAnalytics.Api.Features.Auth;

/// <summary>
/// Applications send their key in <c>X-Api-Key</c>, or in <c>X-Seq-ApiKey</c> (or the <c>apiKey</c> query
/// parameter) when they use a Seq client.
/// </summary>
internal sealed class SourceKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LogsDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string HeaderName = "X-Api-Key";
    public const string SeqHeaderName = "X-Seq-ApiKey";
    public const string SourceIdClaim = "source_id";
    public const string SourceNameClaim = "source_name";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = Request.Headers[HeaderName].FirstOrDefault()
            ?? Request.Headers[SeqHeaderName].FirstOrDefault()
            ?? Request.Query["apiKey"].FirstOrDefault();
        if (string.IsNullOrEmpty(key))
        {
            return AuthenticateResult.NoResult();
        }

        var prefix = ApiKey.PrefixOf(key);
        if (prefix is null)
        {
            return AuthenticateResult.Fail("The API key is malformed.");
        }

        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(s => s.KeyPrefix == prefix, Context.RequestAborted);
        if (source is null || !source.Accepts(key))
        {
            return AuthenticateResult.Fail("The API key is unknown or revoked.");
        }

        var identity = new ClaimsIdentity(
            [new Claim(SourceIdClaim, source.Id.ToString()), new Claim(SourceNameClaim, source.Name)],
            AuthSetup.SourceKeyScheme, SourceNameClaim, null);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), AuthSetup.SourceKeyScheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"ApiKey header=\"{HeaderName}\"";
        return Task.CompletedTask;
    }
}
