namespace TechieRag.Models;

/// <summary>Defines a tool/function that the LLM can call.</summary>
public class ToolDefinition
{
    /// <summary>Gets or sets the tool name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets a description of what the tool does.</summary>
    public required string Description { get; set; }

    /// <summary>Gets or sets the JSON Schema describing the tool's parameters.</summary>
    public required string ParametersSchema { get; set; }

    /// <summary>Gets or sets whether this tool requires user confirmation before execution.</summary>
    public bool RequiresConfirmation { get; set; }
}
