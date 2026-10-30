using System.Collections.Concurrent;
using System.Net;

namespace LogAnalytics.IntegrationTests.Support;

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body);

/// <summary>Stands in for webhooks and the notification-service. URLs containing "fail" get a 500.</summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase), body));
        return new HttpResponseMessage(request.RequestUri!.AbsoluteUri.Contains("fail", StringComparison.Ordinal)
            ? HttpStatusCode.InternalServerError
            : HttpStatusCode.OK);
    }
}
