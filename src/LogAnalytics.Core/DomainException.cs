namespace LogAnalytics.Core;

/// <summary>
/// A business rule was broken. <see cref="Code"/> is a stable, machine-readable value that the API
/// returns to callers; the message is for humans and may change.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    /// <summary>Extra facts returned with the problem response, such as the problems found in a batch.</summary>
    public IDictionary<string, object?> Details { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}
