using System.Text.Json;

namespace TechieRag.Models;

/// <summary>Represents a tool/function call requested by the LLM.</summary>
public class ToolCall
{
    /// <summary>Gets or sets the unique ID for this tool call.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the name of the tool to call.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the JSON-serialized arguments for the tool.</summary>
    public required string ArgumentsJson { get; set; }

    /// <summary>Deserializes the arguments to the specified type.</summary>
    public T GetArguments<T>() where T : class =>
        JsonSerializer.Deserialize<T>(ArgumentsJson)
        ?? throw new InvalidOperationException($"Failed to deserialize tool arguments to {typeof(T).Name}");
}
