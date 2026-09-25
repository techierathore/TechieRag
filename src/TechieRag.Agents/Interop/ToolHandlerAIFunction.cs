using System.Text.Json;
using Microsoft.Extensions.AI;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Agents.Interop;

/// <summary>
/// Adapter 2a, one tool: a TechieRag <see cref="ToolDefinition"/> served by an <see cref="IToolHandler"/>,
/// exposed as a Microsoft.Extensions.AI <see cref="AIFunction"/> (REQ-RAG-017 / BRD-85).
/// </summary>
/// <remarks>
/// <para>The raw JSON schema is parsed once, here, so a malformed schema fails when the agent is built
/// rather than when the model first calls the tool. Invocation builds a <see cref="ToolCall"/> and calls
/// <see cref="IToolHandler.ExecuteToolAsync"/>, so whatever wraps the handler — permission-by-absence,
/// <c>GuardedToolHandler</c>, an egress gate, MCP provenance — keeps working unchanged. A failed
/// <see cref="ToolResult"/> returns its <c>Content</c> to the model exactly as the classic loop does.</para>
/// </remarks>
public sealed class ToolHandlerAIFunction : AIFunction
{
    private readonly IToolHandler handler;
    private readonly ToolDefinition definition;
    private readonly JsonElement schema;

    /// <summary>Creates the function.</summary>
    /// <param name="handler">The handler that executes the tool.</param>
    /// <param name="definition">The tool's definition, one of <see cref="IToolHandler.ToolDefinitions"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">The definition's parameters schema is not a JSON object.</exception>
    public ToolHandlerAIFunction(IToolHandler handler, ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(definition);

        this.handler = handler;
        this.definition = definition;
        schema = ParseSchema(definition);
    }

    /// <inheritdoc/>
    public override string Name => definition.Name;

    /// <inheritdoc/>
    public override string Description => definition.Description;

    /// <inheritdoc/>
    public override JsonElement JsonSchema => schema;

    /// <summary>Gets the TechieRag definition this function exposes.</summary>
    public ToolDefinition Definition => definition;

    /// <inheritdoc/>
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var toolCall = new ToolCall
        {
            Id = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId ?? Guid.NewGuid().ToString("N"),
            Name = definition.Name,
            ArgumentsJson = ChatMessageMapper.SerializeArguments(arguments)
        };

        var result = await handler.ExecuteToolAsync(toolCall, cancellationToken).ConfigureAwait(false);
        ToolResultCapture.Record(result);
        return result.Content;
    }

    private static JsonElement ParseSchema(ToolDefinition definition)
    {
        try
        {
            using var document = JsonDocument.Parse(definition.ParametersSchema);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Tool '{definition.Name}' has a parameters schema that is not valid JSON: {ex.Message}", nameof(definition), ex);
        }

        throw new ArgumentException($"Tool '{definition.Name}' has a parameters schema that is not a JSON object.", nameof(definition));
    }
}
