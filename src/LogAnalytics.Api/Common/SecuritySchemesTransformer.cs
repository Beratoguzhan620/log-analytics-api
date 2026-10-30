using LogAnalytics.Api.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LogAnalytics.Api.Common;

/// <summary>Declares the session cookie and the source key, and attaches the right one to each operation.</summary>
internal sealed class SecuritySchemesTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    private const string CookieScheme = "Session";
    private const string KeyScheme = "SourceKey";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[CookieScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = AuthSetup.CookieName,
            Description = "Set by POST /api/auth/login; the browser sends it automatically.",
        };
        document.Components.SecuritySchemes[KeyScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = SourceKeyHandler.HeaderName,
            Description = "A source's key, for sending logs only.",
        };
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var authorize = context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().ToList();
        if (authorize.Count == 0)
        {
            return Task.CompletedTask;
        }

        var scheme = authorize.Any(a => a.Policy == AuthSetup.IngestPolicy) ? KeyScheme : CookieScheme;
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(scheme, context.Document)] = [] });
        return Task.CompletedTask;
    }
}
