using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Presents several <see cref="IToolHandler"/> instances to the agent loop as one.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The agent loop takes a single tool handler, but an agent's tools now come
/// from more than one place — locally registered skills on a <see cref="ToolRegistry"/> and the
/// tools of any MCP servers registered for the workspace (REQ-RAG-023). Composing handlers keeps
/// that a caller-side concern instead of teaching <c>AgentLoopRunner</c> about sources.</para>
/// <para><b>Precedence:</b> Handlers are consulted in the order given and the first to declare a
/// tool name owns it. Duplicates from later handlers are hidden from the model entirely, so the
/// name the model sees and the handler that runs can never disagree.</para>
/// <para><b>Ownership:</b> This type does not own or dispose its handlers. The caller that created
/// them decides when they end.</para>
/// </remarks>
public sealed class CompositeToolHandler : IToolHandler
{
    private readonly List<ToolDefinition> definitions = [];
    private readonly Dictionary<string, IToolHandler> routes = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> ToolDefinitions => definitions;

    /// <summary>
    /// Composes the given handlers, earliest first.
    /// </summary>
    /// <param name="handlers">The handlers to compose; nulls are ignored.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="handlers"/> is null.</exception>
    public CompositeToolHandler(IEnumerable<IToolHandler?> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        foreach (var handler in handlers)
        {
            if (handler is null) continue;

            foreach (var definition in handler.ToolDefinitions)
            {
                if (routes.ContainsKey(definition.Name)) continue;

                routes[definition.Name] = handler;
                definitions.Add(definition);
            }
        }
    }

    /// <summary>
    /// Composes the given handlers, earliest first.
    /// </summary>
    /// <param name="handlers">The handlers to compose; nulls are ignored.</param>
    public CompositeToolHandler(params IToolHandler?[] handlers)
        : this((IEnumerable<IToolHandler?>)handlers)
    {
    }

    /// <inheritdoc/>
    public Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        if (!routes.TryGetValue(toolCall.Name, out var handler))
        {
            return Task.FromResult(new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error: Unknown tool '{toolCall.Name}'",
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolCall.Name}' is not registered"
            });
        }

        return handler.ExecuteToolAsync(toolCall, cancellationToken);
    }
}
