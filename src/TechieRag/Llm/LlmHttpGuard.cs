using System.Net;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// Shared HTTP response guard for LLM providers: surfaces rate-limit responses
/// (HTTP 429, and HTTP 503 with a <c>Retry-After</c> header) as <see cref="LlmRateLimitException"/>
/// so the <c>Retry-After</c> hint reaches <see cref="TechieRag.Services.RetryHandler"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Replaces bare <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> calls
/// in providers, which discard the <c>Retry-After</c> response header.</para>
/// <para><b>Code Flow:</b> Called by every LLM provider right after receiving a response.
/// Non-rate-limit failures still throw the standard <see cref="HttpRequestException"/>.</para>
/// </remarks>
internal static class LlmHttpGuard
{
    /// <summary>
    /// Throws <see cref="LlmRateLimitException"/> for rate-limit responses (429, or 503 with
    /// <c>Retry-After</c>), a standard <see cref="HttpRequestException"/> for other failures,
    /// and returns silently on success.
    /// </summary>
    /// <param name="response">The HTTP response to inspect.</param>
    /// <exception cref="LlmRateLimitException">The endpoint signaled rate limiting.</exception>
    /// <exception cref="HttpRequestException">The endpoint returned another non-success status code.</exception>
    internal static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var retryAfter = ParseRetryAfter(response);
        bool isRateLimit = response.StatusCode == HttpStatusCode.TooManyRequests
            || (response.StatusCode == HttpStatusCode.ServiceUnavailable && retryAfter is not null);

        if (isRateLimit)
        {
            throw new LlmRateLimitException(
                $"LLM endpoint returned {(int)response.StatusCode} ({response.StatusCode})." +
                (retryAfter is not null ? $" Retry-After: {retryAfter.Value.TotalSeconds:F0}s." : string.Empty),
                response.StatusCode,
                retryAfter);
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Parses the <c>Retry-After</c> response header, supporting both the delta-seconds form
    /// (e.g. <c>Retry-After: 5</c>) and the HTTP-date form (e.g. <c>Retry-After: Wed, 21 Oct 2026 07:28:00 GMT</c>).
    /// </summary>
    /// <param name="response">The HTTP response whose headers to inspect.</param>
    /// <returns>The requested delay (never negative), or <see langword="null"/> when absent or unparseable.</returns>
    internal static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;

        if (header.Delta is not null)
            return header.Delta.Value > TimeSpan.Zero ? header.Delta.Value : TimeSpan.Zero;

        if (header.Date is not null)
        {
            var delay = header.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}
