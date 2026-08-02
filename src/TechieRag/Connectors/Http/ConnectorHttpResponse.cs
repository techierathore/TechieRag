namespace TechieRag.Connectors.Http;

/// <summary>
/// A response as connectors need to see it (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// A flattened value rather than <see cref="HttpResponseMessage"/> on purpose. The tests for paging,
/// rate limiting and error mapping are the reason this seam exists, and a record with a status, a
/// body and a header bag can be written literally in a test — while faking a disposable
/// <c>HttpResponseMessage</c> graph is enough friction that those tests do not get written.
/// </remarks>
/// <param name="StatusCode">HTTP status code.</param>
/// <param name="Body">Response body decoded as text. Empty for a status with no body.</param>
/// <param name="Headers">Response headers, keyed case-insensitively. Paging cursors and rate-limit budgets live here.</param>
public sealed record ConnectorHttpResponse(
    int StatusCode,
    string Body,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    /// <summary>Gets a value indicating whether the status is in the 2xx range.</summary>
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>Reads one response header.</summary>
    /// <param name="name">Header name, matched case-insensitively.</param>
    /// <returns>The header value, or null when absent.</returns>
    public string? Header(string name)
    {
        if (Headers is null || string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (var pair in Headers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
