namespace TechieRag.Models;

/// <summary>
/// Thrown when an embedding service cannot be reached: the connection is refused, the host is not found, the TLS
/// handshake fails or the request times out (REQ-RAG-170, TrStudio TR-RAG-004).
/// </summary>
/// <remarks>
/// <para><b>Why a type of its own.</b> Before 1.3.0 a stopped Ollama reached the caller of <c>InitializeAsync</c>,
/// <c>SearchAsync</c> or <c>IngestTextAsync</c> as a bare <see cref="HttpRequestException"/> or
/// <see cref="System.Net.Sockets.SocketException"/>, the same type as a refused key or a bad request, so a host
/// that wanted to fall back to keyword matching had to catch everything. This one says "the embedding service is
/// not there" and nothing else.</para>
/// <para><b>Compatible.</b> It is an <see cref="HttpRequestException"/>, so existing <c>catch</c> blocks keep working.
/// A service that answers with an error status still throws a plain <see cref="HttpRequestException"/> with its
/// <see cref="HttpRequestException.StatusCode"/>: it was reached.</para>
/// <para><b>Thrown by</b> every built-in HTTP embedding provider: Ollama, LM Studio, OpenAI-compatible, Azure
/// OpenAI, Google Gemini, Cohere and the generic HTTP provider. The message names the provider and the service's
/// address (scheme, host and port only), never a key.</para>
/// </remarks>
public class EmbeddingUnavailableException : HttpRequestException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="providerName">The embedding provider's <c>Name</c>, for example <c>Ollama</c>.</param>
    /// <param name="message">What happened, naming the service's address.</param>
    /// <param name="innerException">The network failure underneath.</param>
    public EmbeddingUnavailableException(string providerName, string message, Exception? innerException)
        : base(message, innerException)
    {
        ProviderName = providerName;
    }

    /// <summary>Gets the name of the embedding provider whose service could not be reached.</summary>
    public string ProviderName { get; }
}
