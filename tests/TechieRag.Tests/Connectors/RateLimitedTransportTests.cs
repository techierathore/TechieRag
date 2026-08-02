using TechieRag.Connectors;
using TechieRag.Connectors.Http;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-032 / BRD-113: rate limits are the normal case on every host these connectors talk to,
/// so waiting them out — and knowing when not to — is behaviour, not error handling.
/// </summary>
public sealed class RateLimitedTransportTests
{
    /// <summary>A 429 is retried and the eventual success is returned.</summary>
    [Fact]
    public async Task RetriesAfterATooManyRequests()
    {
        var inner = new SequenceTransport(
            new ConnectorHttpResponse(429, "slow down", Headers(("Retry-After", "0"))),
            new ConnectorHttpResponse(200, "ok"));

        var response = await Wrap(inner).GetAsync(new ConnectorHttpRequest("https://example.test/x"));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>A 503 is treated as a throttle, because that is what these hosts use it for.</summary>
    [Fact]
    public async Task RetriesAfterServiceUnavailable()
    {
        var inner = new SequenceTransport(
            new ConnectorHttpResponse(503, "busy"),
            new ConnectorHttpResponse(200, "ok"));

        var response = await Wrap(inner).GetAsync(new ConnectorHttpRequest("https://example.test/x"));

        Assert.Equal(200, response.StatusCode);
    }

    /// <summary>
    /// A 403 whose rate-limit budget is spent is a throttle. The largest repository host reports
    /// exactly this shape rather than 429.
    /// </summary>
    [Fact]
    public void TreatsExhaustedBudgetAsThrottled() =>
        Assert.True(RateLimitedTransport.IsThrottled(
            new ConnectorHttpResponse(403, "", Headers(("X-RateLimit-Remaining", "0")))));

    /// <summary>
    /// A plain 403 is a permission problem, which no amount of waiting fixes. Retrying it would turn
    /// "your token lacks access" into four identical failures spread over minutes.
    /// </summary>
    [Fact]
    public async Task DoesNotRetryAPlainForbidden()
    {
        var inner = new SequenceTransport(new ConnectorHttpResponse(403, "no access"));

        var response = await Wrap(inner).GetAsync(new ConnectorHttpRequest("https://example.test/x"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>A host that stays throttled ends the run with a 429, rather than retrying forever.</summary>
    [Fact]
    public async Task GivesUpAfterMaxAttempts()
    {
        var inner = new SequenceTransport(
            Enumerable.Repeat(new ConnectorHttpResponse(429, "", Headers(("Retry-After", "0"))), 10).ToArray());

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => Wrap(inner).GetAsync(new ConnectorHttpRequest("https://example.test/x")));

        Assert.Equal(429, error.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    /// <summary>
    /// A host asking to be left alone for an hour is telling the caller the run is over. Sleeping on
    /// it would hang a background job indistinguishably from a crash.
    /// </summary>
    [Fact]
    public async Task RefusesToWaitPastTheCeiling()
    {
        var inner = new SequenceTransport(
            new ConnectorHttpResponse(429, "", Headers(("Retry-After", "3600"))));

        var transport = Wrap(inner);
        transport.MaxDelay = TimeSpan.FromSeconds(30);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => transport.GetAsync(new ConnectorHttpRequest("https://example.test/x")));

        Assert.Equal(429, error.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>Retry-After as delta seconds is read as delta seconds.</summary>
    [Fact]
    public void ReadsRetryAfterSeconds()
    {
        var transport = Wrap(new SequenceTransport());

        var delay = transport.ResolveDelay(new ConnectorHttpResponse(429, "", Headers(("Retry-After", "45"))));

        Assert.Equal(TimeSpan.FromSeconds(45), delay);
    }

    /// <summary>Retry-After as an HTTP date is read against the clock.</summary>
    [Fact]
    public void ReadsRetryAfterHttpDate()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var transport = new RateLimitedTransport(new SequenceTransport(), null, new FixedTimeProvider(now));

        var delay = transport.ResolveDelay(
            new ConnectorHttpResponse(429, "", Headers(("Retry-After", "Thu, 01 Jan 2026 12:01:00 GMT"))));

        Assert.Equal(TimeSpan.FromMinutes(1), delay);
    }

    /// <summary>
    /// A reset header above 10^9 is a Unix timestamp, not a delta — the two encodings are in use on
    /// different hosts, and reading a timestamp as a delta would ask for a thirty-year wait.
    /// </summary>
    [Fact]
    public void ReadsRateLimitResetAsATimestamp()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reset = now.AddMinutes(5).ToUnixTimeSeconds().ToString();
        var transport = new RateLimitedTransport(new SequenceTransport(), null, new FixedTimeProvider(now));

        var delay = transport.ResolveDelay(
            new ConnectorHttpResponse(403, "", Headers(("X-RateLimit-Reset", reset))));

        Assert.Equal(TimeSpan.FromMinutes(5), delay);
    }

    /// <summary>A small reset value is a delta in seconds.</summary>
    [Fact]
    public void ReadsSmallRateLimitResetAsADelta()
    {
        var transport = Wrap(new SequenceTransport());

        var delay = transport.ResolveDelay(new ConnectorHttpResponse(429, "", Headers(("RateLimit-Reset", "20"))));

        Assert.Equal(TimeSpan.FromSeconds(20), delay);
    }

    /// <summary>A response with no timing hint yields no delay, leaving the caller's backoff to apply.</summary>
    [Fact]
    public void ReturnsNoDelayWhenTheHostDidNotSay() =>
        Assert.Null(Wrap(new SequenceTransport()).ResolveDelay(new ConnectorHttpResponse(429, "")));

    /// <summary>A successful response passes straight through untouched.</summary>
    [Fact]
    public async Task PassesSuccessThrough()
    {
        var inner = new SequenceTransport(new ConnectorHttpResponse(200, "body"));

        var response = await Wrap(inner).GetAsync(new ConnectorHttpRequest("https://example.test/x"));

        Assert.Equal("body", response.Body);
        Assert.Equal(1, inner.Calls);
    }

    private static RateLimitedTransport Wrap(SequenceTransport inner) =>
        new(inner) { MaxAttempts = 3, DefaultDelay = TimeSpan.Zero };

    private static Dictionary<string, string> Headers(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    private sealed class SequenceTransport : IConnectorTransport
    {
        private readonly ConnectorHttpResponse[] responses;

        public SequenceTransport(params ConnectorHttpResponse[] responses) => this.responses = responses;

        public int Calls { get; private set; }

        public Task<ConnectorHttpResponse> GetAsync(
            ConnectorHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = responses[Math.Min(Calls, responses.Length - 1)];
            Calls++;
            return Task.FromResult(response);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedTimeProvider(DateTimeOffset now) => this.now = now;

        public override DateTimeOffset GetUtcNow() => now;
    }
}
