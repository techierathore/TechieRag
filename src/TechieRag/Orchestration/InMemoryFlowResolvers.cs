namespace TechieRag.Orchestration;

/// <summary>
/// An <see cref="IFlowAgentResolver"/> over a fixed set of agents (REQ-RAG-042).
/// </summary>
/// <remarks>
/// The straightforward case: a host that has already built its agents for this turn and wants the
/// flow to use exactly those. A host whose agents live in a database implements the interface
/// directly instead of loading everything up front.
/// </remarks>
public sealed class InMemoryFlowAgentResolver : IFlowAgentResolver
{
    private readonly Dictionary<string, FlowAgent> agents = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a resolver over the given agents.
    /// </summary>
    /// <param name="agents">The agents to expose; a later duplicate id replaces an earlier one.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agents"/> is null.</exception>
    public InMemoryFlowAgentResolver(IEnumerable<FlowAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        foreach (var agent in agents)
        {
            this.agents[agent.Id] = agent;
        }
    }

    /// <summary>Creates a resolver over the given agents.</summary>
    /// <param name="agents">The agents to expose.</param>
    public InMemoryFlowAgentResolver(params FlowAgent[] agents)
        : this((IEnumerable<FlowAgent>)agents)
    {
    }

    /// <inheritdoc/>
    public Task<FlowAgent?> ResolveAgentAsync(string agentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(agentId is not null && agents.TryGetValue(agentId, out var agent) ? agent : null);

    /// <inheritdoc/>
    public Task<IReadOnlyList<FlowAgent>> ListAgentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FlowAgent>>(agents.Values.ToList());
}

/// <summary>
/// An <see cref="IFlowGuardrailResolver"/> over a fixed set of guardrails (REQ-RAG-042).
/// </summary>
/// <remarks>
/// An id this resolver was not given resolves to null, which the runner treats as a BLOCK. That is
/// the intended behaviour and not a gap in the double: a flow referring to a check the host does not
/// have must not run as though the check had passed.
/// </remarks>
public sealed class InMemoryFlowGuardrailResolver : IFlowGuardrailResolver
{
    private readonly Dictionary<string, IFlowGuardrail> guardrails = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a resolver over the given guardrails.
    /// </summary>
    /// <param name="guardrails">The guardrails to expose; a later duplicate id replaces an earlier one.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="guardrails"/> is null.</exception>
    public InMemoryFlowGuardrailResolver(IEnumerable<IFlowGuardrail> guardrails)
    {
        ArgumentNullException.ThrowIfNull(guardrails);

        foreach (var guardrail in guardrails)
        {
            this.guardrails[guardrail.Id] = guardrail;
        }
    }

    /// <summary>Creates a resolver over the given guardrails.</summary>
    /// <param name="guardrails">The guardrails to expose.</param>
    public InMemoryFlowGuardrailResolver(params IFlowGuardrail[] guardrails)
        : this((IEnumerable<IFlowGuardrail>)guardrails)
    {
    }

    /// <inheritdoc/>
    public Task<IFlowGuardrail?> ResolveGuardrailAsync(string guardrailId, CancellationToken cancellationToken = default) =>
        Task.FromResult(guardrailId is not null && guardrails.TryGetValue(guardrailId, out var guardrail) ? guardrail : null);

    /// <inheritdoc/>
    public Task<IReadOnlyList<IFlowGuardrail>> ListGuardrailsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IFlowGuardrail>>(guardrails.Values.ToList());
}
