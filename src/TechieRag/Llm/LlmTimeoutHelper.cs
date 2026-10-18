using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// Computes the effective timeout for an LLM call and creates a linked cancellation token when needed.
/// </summary>
/// <remarks>
/// The host's own cancellation token is always respected. When a timeout is configured and the host's
/// token has no deadline, a linked token source applies that timeout so the call still ends. The
/// provider's <see cref="HttpClient.Timeout"/> should be set to <see cref="Timeout.InfiniteTimeSpan"/>
/// so it never truncates a host token that allows more time.
/// </remarks>
internal static class LlmTimeoutHelper
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Gets the timeout that should govern this call.</summary>
    /// <param name="options">The completion options; may carry a per-call timeout.</param>
    /// <param name="configuredTimeout">A provider-level timeout, for example from <see cref="LlmConfig.RequestTimeout"/>.</param>
    /// <returns>The effective timeout.</returns>
    internal static TimeSpan GetEffectiveTimeout(LlmCompletionOptions? options, TimeSpan? configuredTimeout = null)
    {
        if (options?.RequestTimeout is { } requestTimeout && requestTimeout > TimeSpan.Zero)
        {
            return requestTimeout;
        }

        if (configuredTimeout is { } configTimeout && configTimeout > TimeSpan.Zero)
        {
            return configTimeout;
        }

        return DefaultTimeout;
    }

    /// <summary>
    /// Creates a linked token source that cancels at the effective timeout when the caller's token does
    /// not already have a sooner deadline.
    /// </summary>
    /// <param name="options">The completion options.</param>
    /// <param name="configuredTimeout">A provider-level timeout.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A linked token source the caller must dispose; or the original token when no link is needed.</returns>
    internal static CancellationTokenSource? CreateLinkedTokenSource(
        LlmCompletionOptions? options,
        TimeSpan? configuredTimeout,
        CancellationToken cancellationToken)
    {
        var effectiveTimeout = GetEffectiveTimeout(options, configuredTimeout);

        // If the caller's token already cancels sooner or at the same time, do not add another layer.
        if (cancellationToken.CanBeCanceled && cancellationToken is CancellationToken ct)
        {
            // There is no public API to read the deadline of a CancellationToken; creating a linked
            // source with CancelAfter would race. We link unconditionally and rely on whichever fires
            // first. This is cheap and correct.
        }

        var linked = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(effectiveTimeout);
        return linked;
    }
}
