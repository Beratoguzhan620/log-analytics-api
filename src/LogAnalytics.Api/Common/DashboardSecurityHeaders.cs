namespace LogAnalytics.Api.Common;

/// <summary>
/// The dashboard shows text that applications wrote, so it gets a strict content security policy: scripts,
/// fonts and data only from this origin, and no framing. It renders log text as text, never as HTML, and the
/// policy is the second line of defence. The API reference keeps its own needs and is left alone.
/// </summary>
internal sealed class DashboardSecurityHeaders(RequestDelegate next)
{
    private const string Policy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/scalar") && !path.StartsWithSegments("/openapi"))
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = Policy;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers.XFrameOptions = "DENY";
                return Task.CompletedTask;
            });
        }

        return next(context);
    }
}
