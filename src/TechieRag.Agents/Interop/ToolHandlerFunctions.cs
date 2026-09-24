using Microsoft.Extensions.AI;
using TechieRag.Abstractions;

namespace TechieRag.Agents.Interop;

/// <summary>
/// Adapter 2a: a TechieRag <see cref="IToolHandler"/> as Microsoft.Extensions.AI tools
/// (REQ-RAG-017 / BRD-85).
/// </summary>
public static class ToolHandlerFunctions
{
    /// <summary>Exposes every tool of a handler as an <see cref="AITool"/>.</summary>
    /// <param name="handler">The handler: a <c>ToolRegistry</c>, a composite, a guarded or MCP handler.</param>
    /// <returns>
    /// One <see cref="ToolHandlerAIFunction"/> per <see cref="IToolHandler.ToolDefinitions"/> entry; a
    /// definition with <c>RequiresConfirmation</c> is wrapped in <see cref="ApprovalRequiredAIFunction"/>
    /// so Agent Framework's approval flow is available to hosts that want it.
    /// </returns>
    /// <remarks>The definitions are read once, now. A handler whose catalogue changes per turn (an MCP
    /// registry, a permission intersection) is adapted again per turn.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    /// <exception cref="ArgumentException">A definition's parameters schema is not a JSON object.</exception>
    public static IList<AITool> From(IToolHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return handler.ToolDefinitions
            .Select(definition =>
            {
                var function = new ToolHandlerAIFunction(handler, definition);
                return definition.RequiresConfirmation ? (AITool)new ApprovalRequiredAIFunction(function) : function;
            })
            .ToList();
    }
}
