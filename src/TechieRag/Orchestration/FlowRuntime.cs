using TechieRag.Abstractions;

namespace TechieRag.Orchestration;

/// <summary>
/// Resolves the agent id a <see cref="FlowNode"/> names to a live binding (REQ-RAG-042).
/// </summary>
/// <remarks>
/// Asynchronous because a host's agents commonly live in a database and their tools commonly have to
/// be started — an MCP server, for instance, is a process. A synchronous contract would push every
/// host into blocking on a task inside the run.
/// </remarks>
public interface IFlowAgentResolver
{
    /// <summary>Resolves an agent id.</summary>
    /// <param name="agentId">The id named by a flow node.</param>
    /// <param name="cancellationToken">Token cancelled when the run is cancelled.</param>
    /// <returns>The binding, or null when the host has no such agent.</returns>
    Task<FlowAgent?> ResolveAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>Lists the agents a builder UI may offer, for the node editor's agent picker.</summary>
    /// <param name="cancellationToken">Token cancelled when the caller gives up.</param>
    /// <returns>The available agents; empty when the host offers none.</returns>
    Task<IReadOnlyList<FlowAgent>> ListAgentsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the guardrail ids a <see cref="FlowNode"/> names to live checks (REQ-RAG-042).
/// </summary>
/// <remarks>
/// Returning null for a known-to-the-flow id is not an error the runner recovers from: the step is
/// BLOCKED. A missing check is indistinguishable from a check that would have refused, and guessing
/// "probably fine" is the failure this contract exists to prevent.
/// </remarks>
public interface IFlowGuardrailResolver
{
    /// <summary>Resolves a guardrail id.</summary>
    /// <param name="guardrailId">The id named by a flow node.</param>
    /// <param name="cancellationToken">Token cancelled when the run is cancelled.</param>
    /// <returns>The guardrail, or null when the host has no such check — which denies.</returns>
    Task<IFlowGuardrail?> ResolveGuardrailAsync(string guardrailId, CancellationToken cancellationToken = default);

    /// <summary>Lists the guardrails a builder UI may offer, for the node editor's guardrail picker.</summary>
    /// <param name="cancellationToken">Token cancelled when the caller gives up.</param>
    /// <returns>The available guardrails; empty when the host offers none.</returns>
    Task<IReadOnlyList<IFlowGuardrail>> ListGuardrailsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Everything a <see cref="FlowDefinition"/> needs at run time that is deliberately not stored in
/// it — the live agents, the tools, and the host's own non-negotiable guardrails (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>The trust boundary.</b> A flow definition is user data. A runtime is host code. Anything
/// that is a security decision lives here, where a flow author cannot reach it. That is what makes
/// <see cref="HostGuardrails"/> meaningful: they apply to every node and every tool call of every
/// flow this runtime executes, there is no flow-level property that disables them, and deleting a
/// node's own <see cref="FlowNode.GuardrailIds"/> does not touch them.</para>
/// <para><b>How TechieDesk's EgressGate plugs in (REQ-NFR-013).</b> The app builds its
/// <c>EgressGate</c> for the turn as it does today, then adds one
/// <see cref="DelegateFlowGuardrail"/> to <see cref="HostGuardrails"/> that, at
/// <see cref="GuardrailStage.ToolCall"/>, asks the gate about the tool named in the
/// <see cref="GuardrailContext"/> and blocks when the answer is no. Nothing about the gate moves
/// into the library, and no flow can route around it.</para>
/// </remarks>
public sealed class FlowRuntime
{
    /// <summary>
    /// Creates a runtime.
    /// </summary>
    /// <param name="agents">How agent ids resolve. Required — a flow with agent nodes cannot run without it.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agents"/> is null.</exception>
    public FlowRuntime(IFlowAgentResolver agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        Agents = agents;
    }

    /// <summary>Gets the agent resolver.</summary>
    public IFlowAgentResolver Agents { get; }

    /// <summary>
    /// Gets or sets how guardrail ids resolve. Null while some node names a guardrail means every
    /// such node is BLOCKED — a host that forgets to wire its checks gets a visibly stopped flow,
    /// never a silently unchecked one.
    /// </summary>
    public IFlowGuardrailResolver? Guardrails { get; set; }

    /// <summary>
    /// Gets or sets the tools available to <see cref="FlowNodeKind.Tool"/> nodes — the deterministic
    /// steps that run without an LLM. Null makes every tool node fail with an unknown-tool result.
    /// </summary>
    public IToolHandler? Tools { get; set; }

    /// <summary>
    /// Gets the guardrails the HOST imposes, applied to every node's input and output and to every
    /// tool call, in addition to whatever the flow itself names. A flow cannot remove them.
    /// </summary>
    public List<IFlowGuardrail> HostGuardrails { get; } = [];

    /// <summary>
    /// Gets or sets the system prompt prefixed to every agent in this runtime, ahead of the agent's
    /// own. Null adds nothing.
    /// </summary>
    public string? SystemPreamble { get; set; }
}
