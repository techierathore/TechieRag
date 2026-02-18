namespace TechieRag.Models;

/// <summary>Represents a single message in a chat conversation.</summary>
public class ChatMessage
{
    /// <summary>Gets or sets the role of the message sender.</summary>
    public required string Role { get; set; }

    /// <summary>Gets or sets the text content of the message.</summary>
    public string? Content { get; set; }

    /// <summary>Gets or sets tool calls requested by the assistant.</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; set; }

    /// <summary>Gets or sets the tool call ID this message responds to.</summary>
    public string? ToolCallId { get; set; }

    /// <summary>Gets or sets the display name of the sender.</summary>
    public string? Name { get; set; }

    /// <summary>Gets the timestamp when this message was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Creates a system message.</summary>
    public static ChatMessage System(string content) => new() { Role = "system", Content = content };

    /// <summary>Creates a user message.</summary>
    public static ChatMessage User(string content) => new() { Role = "user", Content = content };

    /// <summary>Creates an assistant message.</summary>
    public static ChatMessage Assistant(string content) => new() { Role = "assistant", Content = content };

    /// <summary>Creates a tool result message.</summary>
    public static ChatMessage Tool(string toolCallId, string content) =>
        new() { Role = "tool", ToolCallId = toolCallId, Content = content };
}
