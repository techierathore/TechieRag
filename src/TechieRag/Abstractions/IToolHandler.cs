using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for handling tool/function calls from LLMs.
/// </summary>
public interface IToolHandler
{
    /// <summary>Gets the list of tool definitions available for the LLM to call.</summary>
    IReadOnlyList<ToolDefinition> ToolDefinitions { get; }

    /// <summary>Executes a tool call and returns the result.</summary>
    Task<ToolResult> ExecuteToolAsync(
        ToolCall toolCall,
        CancellationToken cancellationToken = default);
}
