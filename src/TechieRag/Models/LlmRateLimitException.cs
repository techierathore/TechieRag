using System.Net;

namespace TechieRag.Models;

/// <summary>
/// Exception thrown when an LLM endpoint rejects a request due to rate limiting (HTTP 429)
/// or temporary unavailability (HTTP 503) accompanied by a <c>Retry-After</c> hint.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries the server-provided <c>Retry-After</c> delay so that
/// <see cref="TechieRag.Services.RetryHandler"/> can honor the server's requested wait time
/// instead of applying generic exponential backoff.</para>
/// <para><b>Code Flow:</b> Thrown by LLM providers when a non-success status code indicates
/// rate limiting. Derives from <see cref="LlmHttpException"/>, and so from <see cref="HttpRequestException"/>,
/// so pre-existing retry logic that catches <see cref="HttpRequestException"/> keeps working unchanged
/// (backward compatible), and the provider's own text is kept as on every other failure (REQ-RAG-134).</para>
/// </remarks>
public class LlmRateLimitException : LlmHttpException
{
    /// <summary>
    /// Gets the delay requested by the server via the <c>Retry-After</c> response header,
    /// or <see langword="null"/> when the header was absent or unparseable.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Creates a new rate-limit exception.
    /// </summary>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="statusCode">The HTTP status code returned by the endpoint (typically 429 or 503).</param>
    /// <param name="retryAfter">The server-requested retry delay, or <see langword="null"/> when unavailable.</param>
    public LlmRateLimitException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter)
        : this(message, statusCode, retryAfter, responseBody: null, providerMessage: null)
    {
    }

    /// <summary>
    /// Creates a new rate-limit exception that keeps what the provider sent back.
    /// </summary>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="statusCode">The HTTP status code returned by the endpoint (typically 429 or 503).</param>
    /// <param name="retryAfter">The server-requested retry delay, or <see langword="null"/> when unavailable.</param>
    /// <param name="responseBody">The response body, already cut to <see cref="LlmHttpException.MaxBodyLength"/>, or <see langword="null"/>.</param>
    /// <param name="providerMessage">The provider's own error message read from the body, or <see langword="null"/>.</param>
    public LlmRateLimitException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter, string? responseBody, string? providerMessage)
        : base(message, statusCode, responseBody, providerMessage)
    {
        RetryAfter = retryAfter;
    }
}
