namespace TechieRag.Models;

/// <summary>Response from an LLM completion or chat operation.</summary>
public class LlmResponse
{
    /// <summary>Gets or sets the generated text content.</summary>
    public string? Content { get; set; }

    /// <summary>Gets or sets the model's reasoning ("thinking") text, kept out of <see cref="Content"/>;
    /// null when the model sent none (REQ-RAG-137 / REQ-RAG-138).</summary>
    /// <remarks>Filled from the service's own thinking field or blocks (<c>reasoning_content</c>, Anthropic
    /// <c>thinking</c> blocks, Gemini thought parts, Ollama <c>thinking</c>, a reasoning summary), or from a
    /// <c>&lt;think&gt;…&lt;/think&gt;</c> block at the start of the answer. Not part of
    /// <see cref="ToChatMessage"/>: thinking text is not sent back to the model.</remarks>
    public string? Thinking { get; set; }

    /// <summary>Gets or sets data the provider needs back with this turn on the next call; null when it
    /// needs none (REQ-RAG-139).</summary>
    /// <remarks>Opaque. <see cref="ToChatMessage"/> copies it to <see cref="ChatMessage.ProviderState"/>.</remarks>
    public string? ProviderState { get; set; }

    /// <summary>Gets or sets the tool calls requested by the LLM.</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; set; }

    /// <summary>Gets whether the response contains tool calls.</summary>
    public bool HasToolCalls => ToolCalls is { Count: > 0 };

    /// <summary>Gets or sets the token usage for this operation.</summary>
    public required TokenUsage Usage { get; set; }

    /// <summary>Gets or sets the finish reason.</summary>
    public string FinishReason { get; set; } = "stop";

    /// <summary>Gets or sets the model that generated the response.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Converts to a ChatMessage for conversation history.</summary>
    public ChatMessage ToChatMessage() => new()
    {
        Role = "assistant",
        Content = Content,
        ToolCalls = ToolCalls,
        ProviderState = ProviderState
    };
}
