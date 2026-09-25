using System.Runtime.CompilerServices;
using System.Text;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// The default <see cref="ILlmProvider.ChatStreamEventsAsync"/> for providers that do not override it
/// (REQ-RAG-067 / ADR-005).
/// </summary>
internal static class LlmStreamEventFallback
{
    /// <summary>Streams typed events from a provider's older members.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="messages">The conversation.</param>
    /// <param name="options">Completion options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Text deltas, tool calls, then a completed event.</returns>
    public static IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        ILlmProvider provider,
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(messages);

        // A text stream cannot carry a tool call, so a tool-using turn has to be asked whole.
        var needsWholeResponse = options?.Tools is { Count: > 0 } || !provider.SupportsStreaming;
        return needsWholeResponse
            ? FromChatAsync(provider, messages, options, cancellationToken)
            : FromTextStreamAsync(provider, messages, options, cancellationToken);
    }

    private static async IAsyncEnumerable<LlmStreamEvent> FromChatAsync(
        ILlmProvider provider,
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await provider.ChatAsync(messages, options, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(response.Content))
        {
            yield return LlmStreamEvent.FromText(response.Content);
        }

        foreach (var toolCall in response.ToolCalls ?? [])
        {
            yield return LlmStreamEvent.FromToolCall(toolCall);
        }

        var modelName = string.IsNullOrEmpty(response.ModelName) ? provider.ModelName : response.ModelName;
        yield return LlmStreamEvent.FromCompleted(response.Usage, response.FinishReason, modelName);
    }

    private static async IAsyncEnumerable<LlmStreamEvent> FromTextStreamAsync(
        ILlmProvider provider,
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        await foreach (var token in provider.ChatStreamAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            text.Append(token);
            yield return LlmStreamEvent.FromText(token);
        }

        var usage = new TokenUsage
        {
            InputTokens = messages.Sum(m => provider.EstimateTokenCount(m.Content ?? string.Empty)),
            OutputTokens = provider.EstimateTokenCount(text.ToString()),
            ModelName = provider.ModelName,
            ProviderName = provider.Name
        };
        yield return LlmStreamEvent.FromCompleted(usage, "stop", provider.ModelName);
    }
}
