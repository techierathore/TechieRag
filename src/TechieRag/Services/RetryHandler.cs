using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Wraps an ILlmProvider with retry, rate-limit handling, and circuit breaker logic.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides resilience for LLM API calls. Implements exponential backoff,
/// rate-limit detection (HTTP 429) honoring the server's <c>Retry-After</c> header via
/// <see cref="LlmRateLimitException.RetryAfter"/> (capped at <see cref="ResilienceConfig.MaxRetryDelayMs"/>),
/// and circuit breaker pattern.</para>
/// <para><b>Code Flow:</b> Wraps the actual ILlmProvider as a decorator.
/// All calls are delegated to the inner provider with retry logic around them.</para>
/// </remarks>
public class RetryHandler : ILlmProvider
{
    private readonly ILlmProvider inner;
    private readonly ResilienceConfig config;
    private readonly ILogger<RetryHandler> logger;
    private int consecutiveFailures;
    private DateTime circuitOpenedAt = DateTime.MinValue;

    /// <summary>
    /// Test seam: performs the wait between retry attempts. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>;
    /// tests replace it to capture requested delays without real sleeping.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } =
        static (delay, cancellationToken) => Task.Delay(delay, cancellationToken);

    /// <inheritdoc/>
    public string Name => inner.Name;

    /// <inheritdoc/>
    public string ModelName => inner.ModelName;

    /// <inheritdoc/>
    public bool SupportsToolCalling => inner.SupportsToolCalling;

    /// <inheritdoc/>
    public bool SupportsStreaming => inner.SupportsStreaming;

    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted
    {
        add => inner.OnCompletionCompleted += value;
        remove => inner.OnCompletionCompleted -= value;
    }

    /// <summary>
    /// Creates a new retry handler wrapping an LLM provider.
    /// </summary>
    /// <param name="inner">The actual LLM provider to wrap.</param>
    /// <param name="config">Resilience configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public RetryHandler(ILlmProvider inner, ResilienceConfig config, ILogger<RetryHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(config);

        this.inner = inner;
        this.config = config;
        this.logger = logger ?? NullLogger<RetryHandler>.Instance;
    }

    /// <inheritdoc/>
    public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
        => ExecuteWithRetryAsync(() => inner.CompleteAsync(prompt, options, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureCircuitNotOpen();

        await foreach (var token in inner.CompleteStreamAsync(prompt, options, cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }

        RecordSuccess();
    }

    /// <inheritdoc/>
    public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
        => ExecuteWithRetryAsync(() => inner.ChatAsync(messages, options, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureCircuitNotOpen();

        await foreach (var token in inner.ChatStreamAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }

        RecordSuccess();
    }

    /// <inheritdoc/>
    public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class
        => ExecuteWithRetryAsync(() => inner.CompleteAsync<T>(prompt, options, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public int EstimateTokenCount(string text) => inner.EstimateTokenCount(text);

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        EnsureCircuitNotOpen();

        int delay = config.InitialRetryDelayMs;

        for (int attempt = 0; attempt <= config.MaxRetries; attempt++)
        {
            try
            {
                var result = await action().ConfigureAwait(false);
                RecordSuccess();
                return result;
            }
            catch (HttpRequestException ex) when (attempt < config.MaxRetries)
            {
                RecordFailure();

                var wait = TimeSpan.FromMilliseconds(delay);

                if (config.HandleRateLimiting && ex is LlmRateLimitException { RetryAfter: not null } rateLimit)
                {
                    // Honor the server's Retry-After hint (delta-seconds or HTTP-date), capped at the max backoff.
                    wait = TimeSpan.FromMilliseconds(
                        Math.Min(rateLimit.RetryAfter.Value.TotalMilliseconds, config.MaxRetryDelayMs));

                    logger.LogWarning("Rate limited ({StatusCode}). Honoring Retry-After: waiting {Delay}ms before retry {Attempt}/{MaxRetries}",
                        (int?)ex.StatusCode, (int)wait.TotalMilliseconds, attempt + 1, config.MaxRetries);
                }
                else if (config.HandleRateLimiting && ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    logger.LogWarning("Rate limited (429). Waiting {Delay}ms before retry {Attempt}/{MaxRetries}",
                        delay, attempt + 1, config.MaxRetries);
                }
                else
                {
                    logger.LogWarning(ex, "LLM request failed (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}ms",
                        attempt + 1, config.MaxRetries, delay);
                }

                await DelayAsync(wait, cancellationToken).ConfigureAwait(false);
                delay = Math.Min((int)(delay * config.BackoffMultiplier), config.MaxRetryDelayMs);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex) when (attempt < config.MaxRetries)
            {
                RecordFailure();
                logger.LogWarning(ex, "LLM request timed out (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}ms",
                    attempt + 1, config.MaxRetries, delay);

                await DelayAsync(TimeSpan.FromMilliseconds(delay), cancellationToken).ConfigureAwait(false);
                delay = Math.Min((int)(delay * config.BackoffMultiplier), config.MaxRetryDelayMs);
            }
            catch (Exception)
            {
                RecordFailure();
                throw;
            }
        }

        throw new InvalidOperationException("Retry loop exhausted without returning or throwing.");
    }

    private void EnsureCircuitNotOpen()
    {
        if (consecutiveFailures >= config.CircuitBreakerThreshold)
        {
            var elapsed = DateTime.UtcNow - circuitOpenedAt;
            if (elapsed.TotalSeconds < config.CircuitBreakerRecoverySeconds)
            {
                throw new InvalidOperationException(
                    $"Circuit breaker is open after {consecutiveFailures} consecutive failures. " +
                    $"Recovery in {config.CircuitBreakerRecoverySeconds - (int)elapsed.TotalSeconds}s.");
            }

            logger.LogInformation("Circuit breaker recovery period elapsed. Allowing request.");
            Interlocked.Exchange(ref consecutiveFailures, 0);
        }
    }

    private void RecordSuccess()
    {
        Interlocked.Exchange(ref consecutiveFailures, 0);
    }

    private void RecordFailure()
    {
        var failures = Interlocked.Increment(ref consecutiveFailures);
        if (failures == config.CircuitBreakerThreshold)
        {
            circuitOpenedAt = DateTime.UtcNow;
            logger.LogError("Circuit breaker opened after {Failures} consecutive failures", failures);
        }
    }
}
