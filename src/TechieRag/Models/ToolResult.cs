namespace TechieRag.Models;

/// <summary>Represents the result of executing a tool call.</summary>
public class ToolResult
{
    /// <summary>Gets or sets the ID of the tool call this result responds to.</summary>
    public required string ToolCallId { get; set; }

    /// <summary>Gets or sets the result content.</summary>
    public required string Content { get; set; }

    /// <summary>Gets or sets whether the tool execution was successful.</summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>Gets or sets an error message if the tool execution failed.</summary>
    public string? ErrorMessage { get; set; }
}
