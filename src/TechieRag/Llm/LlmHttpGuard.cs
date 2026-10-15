using System.Net;
using System.Text;
using System.Text.Json;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// Shared HTTP response guard for LLM providers: surfaces rate-limit responses
/// (HTTP 429, and HTTP 503 with a <c>Retry-After</c> header) as <see cref="LlmRateLimitException"/>
/// so the <c>Retry-After</c> hint reaches <see cref="TechieRag.Services.RetryHandler"/>, and every other
/// failure as <see cref="LlmHttpException"/> carrying the provider's error text.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Replaces bare <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> calls
/// in providers, which discard the <c>Retry-After</c> response header and the response body
/// (REQ-RAG-012, REQ-RAG-134).</para>
/// <para><b>Code Flow:</b> Called by every LLM provider, the model lister and the OpenAI-compatible speech
/// clients right after receiving a response. On failure it reads the body once, cuts it to
/// <see cref="LlmHttpException.MaxBodyLength"/>, pulls the provider's message out of the usual JSON error
/// fields, and throws with the standard status line followed by that message.</para>
/// </remarks>
internal static class LlmHttpGuard
{
    /// <summary>The longest provider text put into an exception's message, in characters.</summary>
    private const int MaxMessageDetailLength = 500;

    /// <summary>
    /// Throws <see cref="LlmRateLimitException"/> for rate-limit responses (429, or 503 with
    /// <c>Retry-After</c>), an <see cref="LlmHttpException"/> for other failures, and returns silently on success.
    /// Both exceptions carry the response body and the provider's own error message.
    /// </summary>
    /// <param name="response">The HTTP response to inspect.</param>
    /// <param name="cancellationToken">Cancels reading the failed response's body.</param>
    /// <returns>A task that completes when the response is a success.</returns>
    /// <exception cref="LlmRateLimitException">The endpoint signaled rate limiting.</exception>
    /// <exception cref="LlmHttpException">The endpoint returned another non-success status code.</exception>
    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        var providerMessage = ExtractProviderMessage(body);
        var statusLine = $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()}).";
        var detail = DescribeDetail(body, providerMessage);

        var retryAfter = ParseRetryAfter(response);
        bool isRateLimit = response.StatusCode == HttpStatusCode.TooManyRequests
            || (response.StatusCode == HttpStatusCode.ServiceUnavailable && retryAfter is not null);

        if (isRateLimit)
        {
            throw new LlmRateLimitException(
                $"LLM endpoint returned {(int)response.StatusCode} ({response.StatusCode})." +
                (retryAfter is not null ? $" Retry-After: {retryAfter.Value.TotalSeconds:F0}s." : string.Empty) +
                detail,
                response.StatusCode,
                retryAfter,
                body,
                providerMessage);
        }

        throw new LlmHttpException(statusLine + detail, response.StatusCode, body, providerMessage);
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

    /// <summary>
    /// Reads the provider's own error message from a failure body. Recognises <c>{"error":{"message":…}}</c>
    /// (OpenAI-compatible services, Anthropic, Gemini), <c>{"error":"…"}</c> (Ollama, LM Studio),
    /// <c>{"message":…}</c>, <c>{"detail":…}</c> (FastAPI servers) and a top-level array holding one of these.
    /// </summary>
    /// <param name="body">The response body, or <see langword="null"/>.</param>
    /// <returns>The trimmed message, or <see langword="null"/> when the body is not JSON or holds no such field.</returns>
    internal static string? ExtractProviderMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var trimmed = body.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return FindMessage(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the text appended to the status line: the provider's message, or else a short excerpt of a
    /// plain-text body. An HTML error page adds nothing.
    /// </summary>
    /// <param name="body">The response body, or <see langword="null"/>.</param>
    /// <param name="providerMessage">The provider's message, or <see langword="null"/>.</param>
    /// <returns>The text to append, starting with a space, or an empty string.</returns>
    private static string DescribeDetail(string? body, string? providerMessage)
    {
        if (providerMessage is not null)
            return " Provider message: " + Shorten(providerMessage);

        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('<')) return string.Empty;

        return " Response body: " + Shorten(body);
    }

    /// <summary>Reads a failed response's body, cut to <see cref="LlmHttpException.MaxBodyLength"/>.</summary>
    /// <param name="response">The failed response.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The body, or <see langword="null"/> when it is empty or cannot be read.</returns>
    private static async Task<string?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new char[LlmHttpException.MaxBodyLength];
            var read = 0;
            while (read < buffer.Length)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
                if (count == 0) break;
                read += count;
            }

            var body = new string(buffer, 0, read).Trim();
            return body.Length == 0 ? null : body;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or ObjectDisposedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Finds an error message in one JSON element, following the shapes listed on <see cref="ExtractProviderMessage"/>.</summary>
    /// <param name="element">The element to search.</param>
    /// <returns>The trimmed message, or <see langword="null"/>.</returns>
    private static string? FindMessage(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindMessage(item);
                if (found is not null) return found;
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Object) return null;

        if (element.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String) return NonEmpty(error.GetString());
            if (error.ValueKind == JsonValueKind.Object)
            {
                var nested = FindMessage(error);
                if (nested is not null) return nested;
            }
        }

        foreach (var name in new[] { "message", "detail" })
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return NonEmpty(value.GetString());
        }

        return null;
    }

    /// <summary>Returns the trimmed text, or <see langword="null"/> when it is empty.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The trimmed text or <see langword="null"/>.</returns>
    private static string? NonEmpty(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Collapses whitespace to single spaces and cuts the text to <see cref="MaxMessageDetailLength"/>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The shortened text.</returns>
    private static string Shorten(string text)
    {
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxMessageDetailLength ? collapsed : collapsed[..MaxMessageDetailLength] + "…";
    }
}
