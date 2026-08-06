using TechieDesk.Services.Agents;
using TechieRag.Abstractions;
using TechieRag.Orchestration;

namespace TechieDesk.Services.Flows;

/// <summary>
/// Binds the agent ids a flow names to this workspace's real, registered agents (REQ-UI-040).
/// </summary>
/// <remarks>
/// <para><b>The id in a stored flow is the agent's HANDLE.</b> A handle is what the user typed, what
/// chat already uses (<c>@analyst</c>), and what survives an export/import — the surrogate
/// <c>WorkspaceAgentId</c> is a row number that means something different on another machine. So a
/// flow references <c>analyst</c> and this resolver looks it up through
/// <see cref="IAgentRegistry.ResolveAsync"/>, exactly as an <c>@mention</c> does.</para>
/// <para><b>The tools are the host's, composed per run, gated as chat gates them.</b> The resolver
/// does not build tools; it is handed a factory by the caller that already applied
/// <see cref="EgressGate.Guard"/> and the workspace skill intersection. That is deliberate — a
/// second tool-composition path is how a flow ends up with a wider tool set than chat.</para>
/// <para><b>An unknown handle resolves to null, not to a substitute.</b> The runner reports it as a
/// failed node naming the id, which is what lets the builder say "this flow names an agent this
/// workspace does not have" instead of quietly running some other agent.</para>
/// </remarks>
public sealed class AgentRegistryFlowAgentResolver : IFlowAgentResolver
{
    private readonly IAgentRegistry registry;
    private readonly string workspaceId;
    private readonly ILlmProvider llmProvider;
    private readonly Func<AgentDefinition, CancellationToken, Task<IToolHandler>> toolFactory;

    /// <summary>Initializes the resolver for one workspace and one run.</summary>
    /// <param name="registry">The workspace agent registry.</param>
    /// <param name="workspaceId">The workspace whose agents this flow may use.</param>
    /// <param name="llmProvider">The provider every resolved agent answers with.</param>
    /// <param name="toolFactory">
    /// Builds the tools one agent may call. Called once per resolved agent, by the caller that
    /// already knows the turn's egress gate and skill intersection.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="workspaceId"/> is blank.</exception>
    public AgentRegistryFlowAgentResolver(
        IAgentRegistry registry,
        string workspaceId,
        ILlmProvider llmProvider,
        Func<AgentDefinition, CancellationToken, Task<IToolHandler>> toolFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(llmProvider);
        ArgumentNullException.ThrowIfNull(toolFactory);

        this.registry = registry;
        this.workspaceId = workspaceId;
        this.llmProvider = llmProvider;
        this.toolFactory = toolFactory;
    }

    /// <inheritdoc />
    public async Task<FlowAgent?> ResolveAgentAsync(
        string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return null;

        var definition = await registry.ResolveAsync(workspaceId, agentId).ConfigureAwait(false);
        if (definition is null) return null;

        var tools = await toolFactory(definition, cancellationToken).ConfigureAwait(false);
        return ToFlowAgent(definition, tools);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Built for the node editor's agent picker, so the agents come back with NO tools attached: the
    /// picker shows names and never runs anything, and starting an MCP server to paint a dropdown
    /// would be unsolicited egress on a page load (REQ-NFR-008).
    /// </remarks>
    public async Task<IReadOnlyList<FlowAgent>> ListAgentsAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions = await registry.ListAsync(workspaceId).ConfigureAwait(false);
        return definitions.Select(definition => ToFlowAgent(definition, tools: null)).ToList();
    }

    /// <summary>Maps a registered agent onto the library's run-time binding.</summary>
    /// <param name="definition">The stored agent.</param>
    /// <param name="tools">The tools it may call, or null for a listing.</param>
    /// <returns>The binding.</returns>
    private FlowAgent ToFlowAgent(AgentDefinition definition, IToolHandler? tools) =>
        new(definition.Handle, llmProvider, tools)
        {
            DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? "@" + definition.Handle
                : definition.DisplayName,
            Description = definition.Description,
            SystemPrompt = definition.Instructions,
            MaxToolCalls = definition.MaxToolCalls
        };
}
