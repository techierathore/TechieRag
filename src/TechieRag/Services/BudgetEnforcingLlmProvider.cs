using System.Runtime.CompilerServices;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Decorates an <see cref="ILlmProvider"/> so that every call is refused once the session's token or
/// cost budget is spent.
/// </summary>
/// <remarks>
/// <para>The check happens before the underlying provider is asked to do anything, so no network
/// request leaves the process after the budget is gone. The exception is
/// <see cref="TechieRagBudgetExceededException"/> and includes the current usage and the budget.</para>
/// <para>Use this when the host wants hard enforcement; for soft alerting only, subscribe to
/// <see cref="ITokenTracker.OnBudgetAlert"/> instead.</para>
/// </remarks>
public sealed class BudgetEnforcingLlmProvider : ILlmProvider
{
    private readonly ILlmProvider inner;
    private readonly ITokenTracker tracker;

    /// <summary>Creates a provider that enforces a token/cost budget.</summary>
    /// <param name="inner">The provider to decorate.</param>
    /// <param name="tracker">The tracker whose budget is checked.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> or <paramref name="tracker"/> is null.</exception>
    public BudgetEnforcingLlmProvider(ILlmProvider inner, ITokenTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(tracker);
        this.inner = inner;
        this.tracker = tracker;
    }

    /// <inheritdoc/>
    public string Name => inner.Name;

    /// <inheritdoc/>
    public string ModelName => inner.ModelName;

    /// <inheritdoc/>
    public string? Route => inner.Route;

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

    /// <inheritdoc/>
    public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        CheckBudget(options);
        return inner.CompleteAsync(prompt, options, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        CheckBudget(options);
        return inner.CompleteStreamAsync(prompt, options, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        CheckBudget(options);
        return inner.ChatAsync(messages, options, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        CheckBudget(options);
        return inner.ChatStreamAsync(messages, options, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<LlmStreamEvent> ChatStreamEventsAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        CheckBudget(options);
        return inner.ChatStreamEventsAsync(messages, options, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        CheckBudget(options);
        return inner.CompleteAsync<T>(prompt, options, cancellationToken);
    }

    /// <inheritdoc/>
    public int EstimateTokenCount(string text) => inner.EstimateTokenCount(text);

    private void CheckBudget(LlmCompletionOptions? options)
    {
        tracker.ThrowIfBudgetExceeded(options?.CallerTag);
    }
}
