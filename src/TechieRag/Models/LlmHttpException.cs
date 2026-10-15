using System.Net;

namespace TechieRag.Models;

/// <summary>
/// Exception thrown when an LLM, model-listing or speech endpoint answers with a non-success status code.
/// Carries what the provider sent back, so a host can show why the call failed (a wrong model name, a
/// refused key, an exhausted quota) instead of the bare status line.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The standard <see cref="HttpRequestException"/> from
/// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> discards the response body, where every provider
/// explains the failure (REQ-RAG-134, Lekhak feedback TR-RAG-007).</para>
/// <para><b>Code Flow:</b> Thrown by <c>LlmHttpGuard.EnsureSuccessAsync</c>, which every LLM provider, the model
/// lister and the OpenAI-compatible speech clients call right after a response arrives. It derives from
/// <see cref="HttpRequestException"/>, so code that catches that type keeps working, and its
/// <see cref="Exception.Message"/> starts with the same status line as before.</para>
/// </remarks>
public class LlmHttpException : HttpRequestException
{
    /// <summary>
    /// The longest response body kept in <see cref="ResponseBody"/>, in characters; a longer body is cut.
    /// </summary>
    public const int MaxBodyLength = 4000;

    /// <summary>
    /// Creates a new exception for a non-success response.
    /// </summary>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="statusCode">The HTTP status code the endpoint returned.</param>
    /// <param name="responseBody">The response body, already cut to <see cref="MaxBodyLength"/>, or <see langword="null"/> when empty or unreadable.</param>
    /// <param name="providerMessage">The provider's own error message read from the body, or <see langword="null"/> when the body has none.</param>
    public LlmHttpException(string message, HttpStatusCode statusCode, string? responseBody, string? providerMessage)
        : base(message, inner: null, statusCode)
    {
        ResponseBody = responseBody;
        ProviderMessage = providerMessage;
    }

    /// <summary>
    /// Gets the response body the endpoint sent with the failure, cut to <see cref="MaxBodyLength"/> characters,
    /// or <see langword="null"/> when it was empty or could not be read.
    /// </summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// Gets the provider's own explanation, read from the body's usual error fields (<c>error.message</c>,
    /// <c>error</c> as text, <c>message</c> or <c>detail</c>), or <see langword="null"/> when the body holds none.
    /// For example <c>"Specified model not found: wrong-model"</c>.
    /// </summary>
    public string? ProviderMessage { get; }
}
