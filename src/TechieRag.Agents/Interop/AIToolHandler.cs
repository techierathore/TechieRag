using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Agents.Interop;

/// <summary>
/// Adapter 2b: Microsoft.Extensions.AI functions, or a whole Agent Framework agent, as a TechieRag
/// <see cref="IToolHandler"/> (REQ-RAG-017 / BRD-85).
/// </summary>
/// <remarks>
/// This is what lets a MAF agent be a node or a tool of the classic loop: <c>AgentLoopRunner</c>,
/// <c>FlowRunner</c> Agent/Tool nodes and <c>FlowRuntime.HostGuardrails</c> all see an ordinary
/// <see cref="IToolHandler"/>. Only <see cref="AIFunction"/>s can be executed; other <see cref="AITool"/>s
/// (hosted tools) have no local implementation and are skipped.
/// </remarks>
public sealed class AIToolHandler : IToolHandler
{
    private readonly Dictionary<string, AIFunction> functions;
    private readonly List<ToolDefinition> definitions;

    /// <summary>Creates a handler over MEAI tools.</summary>
    /// <param name="tools">The tools; non-function tools are skipped.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tools"/> is null.</exception>
    public AIToolHandler(IEnumerable<AITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        functions = new Dictionary<string, AIFunction>(StringComparer.OrdinalIgnoreCase);
        definitions = new List<ToolDefinition>();
        foreach (var function in tools.OfType<AIFunction>())
        {
            functions[function.Name] = function;
            definitions.Add(ChatOptionsMapper.ToDefinition(function));
        }
    }

    /// <summary>Creates a handler that exposes an agent as one tool.</summary>
    /// <param name="agent">The agent.</param>
    /// <param name="session">A session to run it in, or null for a fresh one per call.</param>
    /// <returns>The handler.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    public static AIToolHandler FromAgent(AIAgent agent, AgentSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new AIToolHandler([agent.AsAIFunction(session: session)]);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> ToolDefinitions => definitions;

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        if (!functions.TryGetValue(toolCall.Name, out var function))
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
            var arguments = new AIFunctionArguments(ChatMessageMapper.ParseArguments(toolCall.ArgumentsJson));
            var result = await function.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
            return new ToolResult { ToolCallId = toolCall.Id, Content = ChatMessageMapper.ResultToString(result) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
