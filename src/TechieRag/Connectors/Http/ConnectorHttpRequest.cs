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
    IReadOnlyDictionary<string, string>? Headers = null);
