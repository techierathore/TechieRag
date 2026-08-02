using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Connectors.Http;

/// <summary>
/// Waits out a host's rate limit instead of failing the run (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// <para><b>Rate limits are the normal case, not an edge case.</b> The public repository API allows
/// 5,000 requests an hour, the wiki API throttles per site, and a first full sync of a real source is
/// thousands of requests. A connector that treats 429 as an error does not have a rate-limit bug —
/// it has no rate-limit handling, and it fails on every source big enough to matter.</para>
/// <para><b>A decorator, so it can be tested.</b> Putting the wait loop inside
/// <see cref="HttpConnectorTransport"/> would have made it provable only against a live throttled
/// account. Wrapping the seam instead means the retry count, the header parsing, the ceiling and the
/// give-up path are all driven by a fake inner transport and a fake clock.</para>
/// <para><b>There is a ceiling on waiting.</b> A host that says "come back in 47 minutes" is telling
/// the caller the run is over; sleeping on it would hang a background job for the better part of an
/// hour with no way for the operator to tell it apart from a hang. Past
/// <see cref="MaxDelay"/> the run ends with a <see cref="ConnectorException"/> carrying status 429,
/// which the caller can schedule a retry from.</para>
/// </remarks>
public sealed class RateLimitedTransport : IConnectorTransport
{
    private readonly IConnectorTransport inner;
    private readonly ILogger<RateLimitedTransport> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="RateLimitedTransport"/> class.</summary>
    /// <param name="inner">The transport that performs the request.</param>
    /// <param name="logger">Diagnostics.</param>
    /// <param name="timeProvider">Clock, so waiting is testable without real waiting.</param>
    public RateLimitedTransport(
        IConnectorTransport inner,
        ILogger<RateLimitedTransport>? logger = null,
        TimeProvider? timeProvider = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.logger = logger ?? NullLogger<RateLimitedTransport>.Instance;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets or sets how many times one request may be retried after a throttle.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Gets or sets the longest the transport will wait for one retry.</summary>
    /// <remarks>Beyond this the run ends with a 429 <see cref="ConnectorException"/> rather than sleeping.</remarks>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets or sets the wait used when the host throttles without saying for how long.</summary>
    /// <remarks>Doubled on each further attempt.</remarks>
    public TimeSpan DefaultDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task<ConnectorHttpResponse> GetAsync(
        ConnectorHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var backoff = DefaultDelay;

        for (var attempt = 1; ; attempt++)
        {
            var response = await inner.GetAsync(request, cancellationToken).ConfigureAwait(false);

            if (!IsThrottled(response))
            {
                return response;
            }

            if (attempt >= MaxAttempts)
            {
                throw new ConnectorException(
                    "http",
                    $"The host is still rate limiting after {attempt} attempts (status {response.StatusCode}). Retry this run later.",
                    429);
            }

            var wait = ResolveDelay(response) ?? backoff;
            if (wait > MaxDelay)
            {
                throw new ConnectorException(
                    "http",
                    $"The host asked to be left alone for {wait.TotalMinutes:N0} minutes, longer than the {MaxDelay.TotalMinutes:N0}-minute ceiling. Retry this run later.",
                    429);
            }

            logger.LogWarning(
                "Rate limited (status {Status}); waiting {Seconds:N0}s before attempt {Attempt}",
                response.StatusCode,
                wait.TotalSeconds,
                attempt + 1);

            await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
            backoff += backoff;
        }
    }

    /// <summary>Determines whether a response means "you are being throttled".</summary>
    /// <param name="response">The response to classify.</param>
    /// <returns>True when the request should be retried after a wait.</returns>
    /// <remarks>
    /// <para>429 and 503 are the honest signals. 403 is included only when a rate-limit header says
    /// the budget is spent, because 403 otherwise means a permission problem that no amount of
    /// waiting fixes — and retrying that would turn "your token lacks access" into four identical
    /// failures two minutes apart. The largest repository host returns exactly that shape: 403 with
    /// <c>X-RateLimit-Remaining: 0</c>.</para>
    /// </remarks>
    public static bool IsThrottled(ConnectorHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode is 429 or 503)
        {
            return true;
        }

        if (response.StatusCode != 403)
        {
            return false;
        }

        var remaining = response.Header("X-RateLimit-Remaining") ?? response.Header("RateLimit-Remaining");
        return remaining is not null
            && int.TryParse(remaining, NumberStyles.Integer, CultureInfo.InvariantCulture, out var left)
            && left <= 0;
    }

    /// <summary>Reads how long the host asked to be left alone.</summary>
    /// <param name="response">The throttled response.</param>
    /// <returns>The wait the host asked for, or null when it did not say.</returns>
    /// <remarks>
    /// Three encodings are in play across these hosts: <c>Retry-After</c> as delta seconds,
    /// <c>Retry-After</c> as an HTTP date, and <c>X-RateLimit-Reset</c> as either a Unix timestamp
    /// or a delta. A reset value above 10^9 is a timestamp — that threshold is the year 2001 read as
    /// epoch seconds, and no host asks for a 30-year wait.
    /// </remarks>
    public TimeSpan? ResolveDelay(ConnectorHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var now = timeProvider.GetUtcNow();
        var retryAfter = response.Header("Retry-After");

        if (retryAfter is not null)
        {
            if (double.TryParse(retryAfter, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(Math.Max(0, seconds));
            }

            if (DateTimeOffset.TryParse(
                    retryAfter, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var when))
            {
                return when > now ? when - now : TimeSpan.Zero;
            }
        }

        var reset = response.Header("X-RateLimit-Reset") ?? response.Header("RateLimit-Reset");
        if (reset is null
            || !double.TryParse(reset, NumberStyles.Float, CultureInfo.InvariantCulture, out var resetValue))
        {
            return null;
        }

        if (resetValue > 1_000_000_000)
        {
            var resetAt = DateTimeOffset.FromUnixTimeSeconds((long)resetValue);
            return resetAt > now ? resetAt - now : TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(Math.Max(0, resetValue));
    }
}
