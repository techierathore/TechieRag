namespace TechieRag.Orchestration;

/// <summary>
/// The outcome of running a node's guardrails over one payload (REQ-RAG-042).
/// </summary>
/// <param name="IsAllowed">Whether the guarded step may proceed.</param>
/// <param name="Stage">The stage that was evaluated.</param>
/// <param name="GuardrailId">The guardrail that refused; null when allowed.</param>
/// <param name="Reason">Why it refused; null when allowed.</param>
public sealed record GuardrailVerdict(bool IsAllowed, GuardrailStage Stage, string? GuardrailId, string? Reason)
{
    /// <summary>Gets the verdict used when nothing objected.</summary>
    /// <param name="stage">The stage that was evaluated.</param>
    /// <returns>An allowing verdict.</returns>
    public static GuardrailVerdict Allow(GuardrailStage stage) => new(true, stage, null, null);
}

/// <summary>
/// The guardrails that apply to one node, and the single place they are run (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>Why one chain type and not two call sites.</b> Node input, node output and every tool
/// call are guarded by the same set with the same deny-by-default rules. Written twice, the two
/// copies drift and one of them ends up treating a thrown guardrail as "carry on". This is the only
/// code in the library that decides whether a guarded step proceeds.</para>
/// <para><b>Ordering.</b> The host's guardrails run FIRST, before any the flow named. A flow-author
/// check cannot short-circuit a host check, and a host refusal is what the trace records.</para>
/// <para><b>Every failure mode denies:</b> a guardrail that throws, a guardrail id the resolver
/// cannot produce, and a node naming guardrails with no resolver configured at all. The last one is
/// modelled as an unresolvable id rather than as a special case, so it goes through the same
/// reporting path and shows up in the trace with the id that could not be loaded.</para>
/// </remarks>
public sealed class FlowGuardrailChain
{
    private readonly IReadOnlyList<IFlowGuardrail> guardrails;

    private FlowGuardrailChain(IReadOnlyList<IFlowGuardrail> guardrails) => this.guardrails = guardrails;

    /// <summary>Gets a chain that permits everything, for nodes with no guardrails at all.</summary>
    public static FlowGuardrailChain Empty { get; } = new([]);

    /// <summary>Gets the guardrails in this chain, host checks first.</summary>
    public IReadOnlyList<IFlowGuardrail> Guardrails => guardrails;

    /// <summary>
    /// Builds the chain for one node: the runtime's host guardrails, then the node's own.
    /// </summary>
    /// <param name="runtime">The host runtime supplying the resolver and the host guardrails.</param>
    /// <param name="node">The node whose guardrail ids are resolved.</param>
    /// <param name="cancellationToken">Token cancelled when the run is cancelled.</param>
    /// <returns>The chain to evaluate for this node.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <remarks>
    /// A node id that cannot be resolved is replaced by a guardrail that always refuses, carrying
    /// the unresolvable id. Skipping it would mean a typo in stored data silently removes a check.
    /// </remarks>
    public static async Task<FlowGuardrailChain> BuildAsync(
        FlowRuntime runtime, FlowNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(node);

        var chain = new List<IFlowGuardrail>(runtime.HostGuardrails);

        foreach (var id in node.GuardrailIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;

            var resolved = runtime.Guardrails is null
                ? null
                : await runtime.Guardrails.ResolveGuardrailAsync(id, cancellationToken).ConfigureAwait(false);

            chain.Add(resolved ?? UnresolvableGuardrail(id, runtime.Guardrails is null));
        }

        return chain.Count == 0 ? Empty : new FlowGuardrailChain(chain);
    }

    /// <summary>
    /// Runs every guardrail interested in this stage, stopping at the first refusal.
    /// </summary>
    /// <param name="context">What is about to happen, or what just did.</param>
    /// <param name="cancellationToken">Token cancelled when the run is cancelled.</param>
    /// <returns>The verdict; a refusal names the guardrail and the reason.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the RUN is cancelled — never when a guardrail itself faults.</exception>
    public async Task<GuardrailVerdict> EvaluateAsync(
        GuardrailContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var guardrail in guardrails)
        {
            if (!guardrail.Stages.Contains(context.Stage)) continue;

            cancellationToken.ThrowIfCancellationRequested();

            GuardrailDecision decision;
            try
            {
                decision = await guardrail.InspectAsync(context, cancellationToken).ConfigureAwait(false)
                    ?? GuardrailDecision.Block("The guardrail returned no decision.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Deny by default. A check that cannot run is not a check that passed, and a broken
                // guardrail must never be the cheapest way past it.
                decision = GuardrailDecision.Block(
                    $"The guardrail could not complete ({ex.GetType().Name}: {ex.Message}), so the step was refused.");
            }

            if (!decision.IsAllowed)
            {
                return new GuardrailVerdict(false, context.Stage, guardrail.Id, decision.Reason);
            }
        }

        return GuardrailVerdict.Allow(context.Stage);
    }

    /// <summary>Builds the always-refusing stand-in for a guardrail id that could not be produced.</summary>
    /// <param name="id">The id the flow named.</param>
    /// <param name="isResolverMissing">True when the host configured no resolver at all.</param>
    /// <returns>A guardrail that refuses every stage, naming the id.</returns>
    private static IFlowGuardrail UnresolvableGuardrail(string id, bool isResolverMissing) =>
        new DelegateFlowGuardrail(
            id,
            "Unresolvable guardrail — denies by default.",
            null,
            (_, _) => Task.FromResult(GuardrailDecision.Block(
                isResolverMissing
                    ? $"The flow requires guardrail '{id}' but this host configured no guardrail resolver, so the step was refused."
                    : $"The flow requires guardrail '{id}' but it could not be loaded, so the step was refused.")));
}
