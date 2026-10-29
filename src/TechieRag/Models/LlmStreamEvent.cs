namespace TechieRag.Models;

/// <summary>
/// One typed event of a streamed chat turn, yielded by
/// <see cref="Abstractions.ILlmProvider.ChatStreamEventsAsync"/> (REQ-RAG-067 / BRD-110).
/// </summary>
/// <remarks>
/// <para><b>Order.</b> A stream is zero or more <see cref="LlmStreamEventKind.TextDelta"/> events,
/// with any <see cref="LlmStreamEventKind.Thinking"/> events among them (a reasoning model normally sends
/// all of its thinking before the answer), then zero or more <see cref="LlmStreamEventKind.ToolCall"/> events, then exactly one
/// <see cref="LlmStreamEventKind.Completed"/> event, which is always last. A tool call is emitted
/// only once it is complete: providers whose vendor streams tool-call fragments (OpenAI-style
/// services, Anthropic) assemble them first; the others emit it whole.</para>
/// <para><b>Why a new type rather than widening <c>ChatStreamAsync</c>.</b> <c>ChatStreamAsync</c>
/// yields strings and is implemented by consumers of a published package; changing its element type
/// would break them (ADR-005). The text-only method is now a projection of this one.</para>
/// </remarks>
public sealed class LlmStreamEvent
{
    /// <summary>Gets what this event carries.</summary>
    public required LlmStreamEventKind Kind { get; init; }

    /// <summary>Gets the text fragment for <see cref="LlmStreamEventKind.TextDelta"/> and
    /// <see cref="LlmStreamEventKind.Thinking"/>; null otherwise.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the decided tool call for <see cref="LlmStreamEventKind.ToolCall"/>; null otherwise.</summary>
    public ToolCall? ToolCall { get; init; }

    /// <summary>Gets the token usage for <see cref="LlmStreamEventKind.Completed"/>; null otherwise.</summary>
    /// <remarks>Estimated from text length when the service reported no usage, exactly as the
    /// string streaming methods always did.</remarks>
    public TokenUsage? Usage { get; init; }

    /// <summary>Gets the finish reason for <see cref="LlmStreamEventKind.Completed"/> (<c>stop</c>,
    /// <c>tool_calls</c>, <c>length</c>, ...); null otherwise.</summary>
    public string? FinishReason { get; init; }

    /// <summary>Gets the model that produced the stream, on <see cref="LlmStreamEventKind.Completed"/>; null otherwise.</summary>
    public string? ModelName { get; init; }

    /// <summary>Gets data the provider needs back with this turn on the next call, on
    /// <see cref="LlmStreamEventKind.Completed"/>; null otherwise (REQ-RAG-139).</summary>
    /// <remarks>Opaque; see <see cref="ChatMessage.ProviderState"/>.</remarks>
    public string? ProviderState { get; init; }

    /// <summary>Creates a text-delta event.</summary>
    /// <param name="text">The text fragment.</param>
    /// <returns>A <see cref="LlmStreamEventKind.TextDelta"/> event.</returns>
    public static LlmStreamEvent FromText(string text) =>
        new() { Kind = LlmStreamEventKind.TextDelta, Text = text };

    /// <summary>Creates a thinking event (REQ-RAG-137).</summary>
    /// <param name="text">The reasoning fragment.</param>
    /// <returns>A <see cref="LlmStreamEventKind.Thinking"/> event.</returns>
    public static LlmStreamEvent FromThinking(string text) =>
        new() { Kind = LlmStreamEventKind.Thinking, Text = text };

    /// <summary>Creates a tool-call event.</summary>
    /// <param name="toolCall">The complete tool call.</param>
    /// <returns>A <see cref="LlmStreamEventKind.ToolCall"/> event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toolCall"/> is null.</exception>
    public static LlmStreamEvent FromToolCall(ToolCall toolCall)
    {
        ArgumentNullException.ThrowIfNull(toolCall);
        return new() { Kind = LlmStreamEventKind.ToolCall, ToolCall = toolCall };
    }

    /// <summary>Creates the completed event that ends a stream.</summary>
    /// <param name="usage">Token usage for the whole call.</param>
    /// <param name="finishReason">Why the model stopped.</param>
    /// <param name="modelName">The model that answered.</param>
    /// <returns>A <see cref="LlmStreamEventKind.Completed"/> event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="usage"/> is null.</exception>
    public static LlmStreamEvent FromCompleted(TokenUsage usage, string finishReason, string modelName) =>
        FromCompleted(usage, finishReason, modelName, providerState: null);

    /// <summary>Creates the completed event that ends a stream, with state the provider needs back.</summary>
    /// <param name="usage">Token usage for the whole call.</param>
    /// <param name="finishReason">Why the model stopped.</param>
    /// <param name="modelName">The model that answered.</param>
    /// <param name="providerState">Opaque data for the next call, or null (REQ-RAG-139).</param>
    /// <returns>A <see cref="LlmStreamEventKind.Completed"/> event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="usage"/> is null.</exception>
    public static LlmStreamEvent FromCompleted(TokenUsage usage, string finishReason, string modelName, string? providerState)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return new()
        {
            Kind = LlmStreamEventKind.Completed,
            Usage = usage,
            FinishReason = finishReason,
            ModelName = modelName,
            ProviderState = providerState
        };
    }
}
