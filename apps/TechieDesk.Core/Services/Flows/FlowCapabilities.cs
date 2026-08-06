using TechieRag.Orchestration;

namespace TechieDesk.Services.Flows;

/// <summary>
/// What a flow actually needs from the host before it can run (REQ-UI-040 / BRD-92).
/// </summary>
/// <remarks>
/// <para><b>Why this is not "every flow needs a model".</b> Three of the five node kinds — branch,
/// deterministic tool and end — cost no tokens and make no model request; the catalogue says so on
/// <see cref="FlowNodeKindDescriptor.UsesLlm"/>. Refusing to run a branch-and-end flow on an install
/// with no provider configured would be a false refusal, and it is one that was actually observed on
/// the Catalyst head on 2026-08-01 before this check existed.</para>
/// <para><b>Derived from the catalogue, never from a list of kinds.</b> A node kind added by a future
/// library release brings its own <c>UsesLlm</c> answer, so this stays correct without an edit — the
/// same argument that makes the palette enumerate the catalogue rather than copy it.</para>
/// </remarks>
public static class FlowCapabilities
{
    /// <summary>
    /// Decides whether running this flow will call a model.
    /// </summary>
    /// <param name="flow">The flow about to run.</param>
    /// <returns>True when at least one of its steps is a kind that calls a model.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="flow"/> is null.</exception>
    /// <remarks>
    /// A node whose kind has no catalogue entry is treated as NEEDING a model. That is the safe
    /// reading: the alternative is starting a run that fails at the node, having told the user their
    /// unconfigured install was fine.
    /// </remarks>
    public static bool NeedsLlmProvider(FlowDefinition flow)
    {
        ArgumentNullException.ThrowIfNull(flow);

        return flow.Nodes.Any(node => FlowNodeCatalog.Kinds
            .FirstOrDefault(descriptor => descriptor.Kind == node.Kind)
            ?.UsesLlm ?? true);
    }
}
