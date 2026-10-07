using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Registry for dynamically registering tools with delegate-based handlers.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a fluent API for registering tool functions without
/// implementing IToolHandler. Each tool is a name + schema + async delegate.</para>
/// <para><b>Usage:</b></para>
/// <code>
/// builder.WithTools(tools =>
/// {
///     tools.Register("get_weather", "Gets current weather for a city",
///         "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"]}",
///         async (args, ct) => { return "72F, sunny"; });
/// });
/// </code>
/// </remarks>
public class ToolRegistry : IToolHandler
{
    private readonly List<ToolDefinition> definitions = new();
    private readonly Dictionary<string, Func<string, CancellationToken, Task<string>>> handlers = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> ToolDefinitions => definitions;

    /// <summary>
    /// Registers a tool with a delegate handler.
    /// </summary>
    /// <param name="name">Tool name.</param>
    /// <param name="description">Tool description for the LLM.</param>
    /// <param name="parametersSchema">JSON Schema for tool parameters.</param>
    /// <param name="handler">Async function: (argumentsJson, cancellationToken) => resultString.</param>
    /// <remarks>Registering a name that is already registered replaces both its definition and its
    /// handler, keeping the tool's position in <see cref="ToolDefinitions"/> (REQ-RAG-121).</remarks>
    public void Register(string name, string description, string parametersSchema,
        Func<string, CancellationToken, Task<string>> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentException.ThrowIfNullOrEmpty(parametersSchema);
        ArgumentNullException.ThrowIfNull(handler);

        var definition = new ToolDefinition
        {
            Name = name,
            Description = description,
            ParametersSchema = parametersSchema
        };

        // REQ-RAG-121 / BRD-181: a second registration of a name replaces the definition in place, as
        // it always replaced the handler, so the tool list sent to the model names each tool once and
        // describes the handler that will actually run. Names match case-insensitively, like handlers.
        var existing = definitions.FindIndex(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            definitions[existing] = definition;
        }
        else
        {
            definitions.Add(definition);
        }

        handlers[name] = handler;
    }

    /// <summary>
    /// Registers a synchronous tool with a delegate handler.
    /// </summary>
    /// <param name="name">Tool name.</param>
    /// <param name="description">Tool description for the LLM.</param>
    /// <param name="parametersSchema">JSON Schema for tool parameters.</param>
    /// <param name="handler">Synchronous function: (argumentsJson) => resultString.</param>
    public void Register(string name, string description, string parametersSchema,
        Func<string, string> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Register(name, description, parametersSchema,
            (args, _) => Task.FromResult(handler(args)));
    }

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        if (!handlers.TryGetValue(toolCall.Name, out var handler))
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error: Unknown tool '{toolCall.Name}'",
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolCall.Name}' is not registered"
            };
        }

        try
        {
            var result = await handler(toolCall.ArgumentsJson, cancellationToken).ConfigureAwait(false);
            return new ToolResult { ToolCallId = toolCall.Id, Content = result };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error executing tool: {ex.Message}",
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
