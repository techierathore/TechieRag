namespace TechieRag.Models;

/// <summary>Response from an LLM completion or chat operation.</summary>
public class LlmResponse
{
    /// <summary>Gets or sets the generated text content.</summary>
    public string? Content { get; set; }

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
        ToolCalls = ToolCalls
    };
}
