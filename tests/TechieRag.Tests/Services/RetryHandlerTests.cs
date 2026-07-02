using System.Net;
using System.Text;
using TechieRag;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// Unit tests for <see cref="RetryHandler"/> — retry with exponential backoff, backoff cap,
/// HTTP-429 <c>Retry-After</c> handling (delta-seconds and HTTP-date forms), and the
/// circuit breaker state machine (REQ-RAG-012 / BRD-51, BRD-52, BRD-53).
/// All tests replace the internal <see cref="RetryHandler.DelayAsync"/> seam so no test really sleeps.
/// </summary>
public class RetryHandlerTests
{
    /// <summary>
    /// Verifies that two transient HTTP failures are retried with exponential backoff
    /// (1000ms then 2000ms with multiplier 2) and the third attempt's success is returned.
    /// </summary>
    [Fact]
    public async Task TransientFailureRetriesThenSucceeds()
    {
        var provider = new ScriptedLlmProvider(
            new HttpRequestException("boom 1", null, HttpStatusCode.InternalServerError),
            new HttpRequestException("boom 2", null, HttpStatusCode.InternalServerError));
        var (handler, delays) = CreateHandler(provider, new ResilienceConfig
        {
            MaxRetries = 3,
            InitialRetryDelayMs = 1000,
            BackoffMultiplier = 2.0f
        });

        var response = await handler.CompleteAsync("hello");

        Assert.Equal("ok", response.Content);
        Assert.Equal(3, provider.CallCount);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2000) }, delays);
    }

    /// <summary>
    /// Verifies the exponential backoff delay never exceeds <see cref="ResilienceConfig.MaxRetryDelayMs"/>:
    /// with initial 20000ms, multiplier 2 and cap 30000ms, the delays are 20000, 30000, 30000.
    /// </summary>
    [Fact]
    public async Task BackoffDelayIsCappedAtMaxRetryDelay()
    {
        var provider = new ScriptedLlmProvider(
            new HttpRequestException("boom 1"),
            new HttpRequestException("boom 2"),
            new HttpRequestException("boom 3"));
        var (handler, delays) = CreateHandler(provider, new ResilienceConfig
        {
            MaxRetries = 3,
            InitialRetryDelayMs = 20000,
            MaxRetryDelayMs = 30000,
            BackoffMultiplier = 2.0f
        });

        var response = await handler.CompleteAsync("hello");

        Assert.Equal("ok", response.Content);
        Assert.Equal(
            new[] { TimeSpan.FromMilliseconds(20000), TimeSpan.FromMilliseconds(30000), TimeSpan.FromMilliseconds(30000) },
            delays);
    }

    /// <summary>
    /// End-to-end BRD-52 check through a real provider: an HTTP 429 response carrying
    /// <c>Retry-After: 7</c> (delta-seconds form) reaches <see cref="RetryHandler"/> as
    /// <see cref="LlmRateLimitException"/> and the 7-second server hint is used as the retry
    /// delay instead of the 1-second exponential backoff.
    /// </summary>
    [Fact]
    public async Task RetryAfterDeltaSecondsHonoredAsDelay()
    {
        var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        var provider = CreateLmStudioProvider(rateLimited, OkResponse());
        var (handler, delays) = CreateHandler(provider, new ResilienceConfig
        {
            MaxRetries = 3,
            InitialRetryDelayMs = 1000
        });

        var response = await handler.CompleteAsync("hello");

        Assert.Equal("ok", response.Content);
        var delay = Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(7), delay);
    }

    /// <summary>
    /// Verifies the HTTP-date form of <c>Retry-After</c> is honored: a 429 response whose
    /// header points 10 seconds into the future yields a retry delay close to 10 seconds
    /// (well above the 1-second exponential fallback).
    /// </summary>
    [Fact]
    public async Task RetryAfterHttpDateHonoredAsDelay()
    {
        var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter =
            new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(10));
        var provider = CreateLmStudioProvider(rateLimited, OkResponse());
        var (handler, delays) = CreateHandler(provider, new ResilienceConfig
        {
            MaxRetries = 3,
            InitialRetryDelayMs = 1000
        });

        var response = await handler.CompleteAsync("hello");

        Assert.Equal("ok", response.Content);
        var delay = Assert.Single(delays);
        Assert.InRange(delay, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Verifies a server-requested Retry-After larger than the configured maximum backoff
    /// is capped at <see cref="ResilienceConfig.MaxRetryDelayMs"/> (120s hint, 30s cap → 30s wait).
    /// </summary>
    [Fact]
    public async Task RetryAfterIsCappedAtMaxRetryDelay()
    {
        var provider = new ScriptedLlmProvider(
            new LlmRateLimitException("rate limited", HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(120)));
        var (handler, delays) = CreateHandler(provider, new ResilienceConfig
        {
            MaxRetries = 3,
            InitialRetryDelayMs = 1000,
            MaxRetryDelayMs = 30000
        });

        var response = await handler.CompleteAsync("hello");

        Assert.Equal("ok", response.Content);
        var delay = Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(30), delay);
    }

    /// <summary>
    /// Verifies that a 429 with an unparseable <c>Retry-After</c> value falls back to
    /// generic exponential backoff (the configured 1-second initial delay).
    /// </summary>
    [Fact]
    public async Task UnparseableRetryAfterFallsBackToExponentialBackoff()
    {
        var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.TryAddWithoutValidation("Retry-After", "soonish");
        var provider = CreateLmStudioProvider(rateLimited, OkResponse());
        var (handler, delays) = CreateHandler(provider, new ResilienceConfig
        {
            MaxRetries = 3,
            InitialRetryDelayMs = 1000
        });

        var response = await handler.CompleteAsync("hello");

        Assert.Equal("ok", response.Content);
        var delay = Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    /// <summary>
    /// Verifies the circuit breaker opens after the configured consecutive-failure threshold
    /// and then rejects calls fast: with threshold 2 and no retries, the third call throws
    /// the circuit-open <see cref="InvalidOperationException"/> without invoking the inner provider.
    /// </summary>
    [Fact]
    public async Task CircuitBreakerOpensAfterThresholdAndRejectsFast()
    {
        var provider = new ScriptedLlmProvider(
            new HttpRequestException("boom 1"),
            new HttpRequestException("boom 2"));
        var (handler, _) = CreateHandler(provider, new ResilienceConfig
        {
            MaxRetries = 0,
            CircuitBreakerThreshold = 2,
            CircuitBreakerRecoverySeconds = 30
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.CompleteAsync("one"));
        await Assert.ThrowsAsync<HttpRequestException>(() => handler.CompleteAsync("two"));

        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.CompleteAsync("three"));

        Assert.Contains("Circuit breaker is open", rejected.Message);
        Assert.Equal(2, provider.CallCount);
    }

    /// <summary>
    /// Verifies the half-open/recovery path: after the breaker opens (threshold 2) and the
    /// recovery period elapses (0s for the test), the next call is let through, succeeds,
    /// and resets the failure count so following calls also flow normally.
    /// </summary>
    [Fact]
    public async Task CircuitBreakerHalfOpenAllowsRecovery()
    {
        var provider = new ScriptedLlmProvider(
            new HttpRequestException("boom 1"),
            new HttpRequestException("boom 2"));
        var (handler, _) = CreateHandler(provider, new ResilienceConfig
        {
            MaxRetries = 0,
            CircuitBreakerThreshold = 2,
            CircuitBreakerRecoverySeconds = 0
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.CompleteAsync("one"));
        await Assert.ThrowsAsync<HttpRequestException>(() => handler.CompleteAsync("two"));

        var recovered = await handler.CompleteAsync("three");
        var followUp = await handler.CompleteAsync("four");

        Assert.Equal("ok", recovered.Content);
        Assert.Equal("ok", followUp.Content);
        Assert.Equal(4, provider.CallCount);
    }

    private static (RetryHandler Handler, List<TimeSpan> Delays) CreateHandler(ILlmProvider provider, ResilienceConfig config)
    {
        var delays = new List<TimeSpan>();
        var handler = new RetryHandler(provider, config)
        {
            DelayAsync = (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            }
        };
        return (handler, delays);
    }

    private static LmStudioLlmProvider CreateLmStudioProvider(params HttpResponseMessage[] responses)
    {
        var httpClient = new HttpClient(new SequenceHandler(responses)) { BaseAddress = new Uri("http://localhost:1234") };
        return new LmStudioLlmProvider(httpClient, "test-model");
    }

    private static HttpResponseMessage OkResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""
        {
          "id": "chatcmpl-1",
          "model": "test-model",
          "choices": [ { "index": 0, "message": { "role": "assistant", "content": "ok" }, "finish_reason": "stop" } ],
          "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
        }
        """, Encoding.UTF8, "application/json")
    };

    /// <summary>Stub handler that returns a scripted sequence of HTTP responses, one per request.</summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses;

        public SequenceHandler(params HttpResponseMessage[] responses) => this.responses = new Queue<HttpResponseMessage>(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responses.Dequeue());
    }

    /// <summary>
    /// Fake LLM provider that throws a scripted sequence of exceptions, then succeeds with a canned "ok" response.
    /// </summary>
    private sealed class ScriptedLlmProvider : ILlmProvider
    {
        private readonly Queue<Exception> failures;

        public int CallCount { get; private set; }

        public string Name => "Scripted";

        public string ModelName => "scripted-model";

        public bool SupportsToolCalling => false;

        public bool SupportsStreaming => false;

        event EventHandler<LlmCompletionEventArgs>? ILlmProvider.OnCompletionCompleted { add { } remove { } }

        public ScriptedLlmProvider(params Exception[] failures) => this.failures = new Queue<Exception>(failures);

        public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
            => NextAsync();

        public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
            => NextAsync();

        public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class
            => throw new NotSupportedException();

        public int EstimateTokenCount(string text) => text.Length;

        private Task<LlmResponse> NextAsync()
        {
            CallCount++;
            if (failures.Count > 0) throw failures.Dequeue();

            return Task.FromResult(new LlmResponse
            {
                Content = "ok",
                Usage = new TokenUsage { InputTokens = 1, OutputTokens = 1, ModelName = ModelName, ProviderName = Name }
            });
        }
    }
}
