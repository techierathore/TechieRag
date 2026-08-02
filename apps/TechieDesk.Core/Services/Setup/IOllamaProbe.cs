namespace TechieDesk.Services.Setup;

/// <summary>
/// The outcome of probing a local Ollama server (REQ-FN-016).
/// </summary>
/// <param name="Available">Whether an Ollama server responded at the endpoint.</param>
/// <param name="Endpoint">The endpoint that was probed.</param>
/// <param name="Models">The tag/model names discovered (empty when unavailable).</param>
public sealed record OllamaProbeResult(bool Available, string Endpoint, IReadOnlyList<string> Models);

/// <summary>
/// Probes a local Ollama server for its installed models so the first-run wizard can
/// offer them (REQ-FN-016). Implementations MUST degrade gracefully — a missing or
/// unreachable Ollama returns an unavailable result rather than throwing.
/// </summary>
public interface IOllamaProbe
{
    /// <summary>The conventional local Ollama endpoint.</summary>
    public const string DefaultEndpoint = "http://localhost:11434";

    /// <summary>
    /// Probes <c>GET {endpoint}/api/tags</c> and returns the discovered models. Any failure
    /// (connection refused, timeout, non-success status, malformed body) yields an
    /// unavailable result — never an exception.
    /// </summary>
    /// <param name="endpoint">The Ollama base URL to probe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OllamaProbeResult> ProbeAsync(string endpoint, CancellationToken cancellationToken = default);
}
