using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LogAnalytics.Api.Common;

/// <summary>The landing text of the API reference.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Log Analytics",
            Version = "1.0",
            Description = """
                Structured logs from your applications, kept in PostgreSQL: send them, search them, see what is
                going wrong and get told when errors pile up.

                **Two kinds of callers**

                - *Applications* send logs with a source key in `X-Api-Key` (or `X-Seq-ApiKey`). A key can only send.
                - *People* sign in at `POST /api/auth/login`; the session cookie is used for everything else.
                  The dashboard at `/` uses the same endpoints.

                **Errors** follow RFC 9457 problem details with a stable `code`, for example `validation_failed`.
                Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/log-analytics-api/blob/main/LICENSE") },
        };
        return Task.CompletedTask;
    }
}
