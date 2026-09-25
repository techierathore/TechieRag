namespace TechieRag.Connectors.Http;

/// <summary>
/// One request a connector wants made (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// <para><b>Headers carry the credential, never the URL.</b> All three REST APIs behind these
/// connectors also accept a token as a query parameter, and every connector here refuses to use
/// that: a URL is logged by proxies, recorded in the transport's own diagnostics, and pasted into
/// bug reports. A header is not, which is why the transport logs
/// <see cref="Url"/> and never <see cref="Headers"/>.</para>
/// </remarks>
/// <param name="Url">Absolute https URL. Must contain no secret.</param>
/// <param name="Headers">Request headers, including authorization. Never logged, never persisted.</param>
public sealed record ConnectorHttpRequest(
    string Url,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    /// <summary>
    /// Gets the HTTP method, <c>GET</c> unless set (REQ-RAG-083 / BRD-127, TR-RAG-021). A search
    /// API that takes a query body — Confluence CQL, GitHub GraphQL — needs <c>POST</c>.
    /// </summary>
    public string Method { get; init; } = "GET";

    /// <summary>Gets the request body, or null for none. Never logged, like <see cref="Headers"/>.</summary>
    public string? Body { get; init; }

    /// <summary>Gets the body's media type; <c>application/json</c> when a body is sent and this is null.</summary>
    public string? ContentType { get; init; }
}
