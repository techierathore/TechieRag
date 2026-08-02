using TechieDesk.Services.Agents;
using TechieRag.Orchestration;

namespace TechieDesk.Services.Flows;

/// <summary>
/// The guardrails TechieDesk imposes on every flow it runs, which no flow can name, edit or remove
/// (REQ-UI-040 / REQ-NFR-013).
/// </summary>
/// <remarks>
/// <para><b>The defect this closes before it happens.</b> REQ-NFR-013 made
/// <c>AgentDefinition.ConfirmEgress</c> real by wrapping a chat turn's egress skills in
/// <see cref="EgressGate"/>. A flow is a second execution path to the same tools. Had it been built
/// without this, "compose a flow" would have been a supported way to reach an egress-marked tool
/// without the gate — the switch would be honoured in chat and quietly not in flows, which is the
/// exact class of defect (a control that promises something one path does not do) this project has
/// already hit three times.</para>
/// <para><b>Why it is a HOST guardrail and not something the flow names.</b>
/// <see cref="FlowRuntime.HostGuardrails"/> is supplied at run time by host code and applied by
/// <c>FlowGuardrailChain</c> to EVERY node and EVERY tool call, ahead of whatever the flow itself
/// declares. There is no property on <see cref="FlowDefinition"/> that disables it and clearing a
/// node's own <see cref="FlowNode.GuardrailIds"/> does not touch it. Had the gate been offered
/// through <see cref="FlowGuardrailCatalog"/> instead, deleting one string from a stored document
/// would have removed it.</para>
/// <para><b>It composes with, and does not replace, the per-tool wrapping.</b> The tools a flow's
/// agents run are the ones the host built for the turn, already wrapped by
/// <see cref="EgressGate.Guard"/>. This stage is an additional choke point that also covers
/// deterministic <see cref="FlowNodeKind.Tool"/> nodes and MCP tools, so there is one place that sees
/// every call a flow makes.</para>
/// </remarks>
public static class FlowHostGuardrails
{
    /// <summary>The id the egress guardrail reports itself as, in traces and block reasons.</summary>
    /// <remarks>
    /// Prefixed <c>host-</c> so it is visibly not one of the ids a flow author can choose from
    /// <see cref="FlowGuardrailCatalog"/>, and so a trace reader can tell a host refusal from an
    /// author's rule at a glance.
    /// </remarks>
    public const string EgressGuardrailId = "host-egress";

    /// <summary>
    /// Builds the egress guardrail for one flow run, delegating the decision to the turn's gate.
    /// </summary>
    /// <param name="gate">The gate governing this run, built from the agent whose flow is running.</param>
    /// <returns>A guardrail that consults the gate before any tool call is dispatched.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="gate"/> is null.</exception>
    /// <remarks>
    /// <para>Only <see cref="GuardrailStage.ToolCall"/> is claimed: a node's input and output do not
    /// leave the machine, and asking about them would raise a confirmation prompt for something that
    /// sends nothing — which is how a user learns to click through the prompt that matters.</para>
    /// <para>The gate answers once per run and reuses the answer, exactly as it does for a chat turn,
    /// so a flow with six tool nodes raises one prompt rather than six.</para>
    /// </remarks>
    public static DelegateFlowGuardrail Egress(EgressGate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);

        return new DelegateFlowGuardrail(
            EgressGuardrailId,
            "Asks before a tool call leaves this machine, as the agent's egress setting requires.",
            [GuardrailStage.ToolCall],
            async (context, cancellationToken) =>
            {
                var toolName = context.ToolName;
                if (string.IsNullOrWhiteSpace(toolName))
                {
                    // A ToolCall stage with no tool name is not a call this gate can reason about.
                    // It is also not a call the runner makes, so allowing it changes nothing today
                    // and blocking it would refuse a shape that sends nothing.
                    return GuardrailDecision.Allow();
                }

                var isAllowed = await gate.AllowExternalAsync(
                    toolName,
                    toolName,
                    context.ToolDescription ?? string.Empty,
                    cancellationToken).ConfigureAwait(false);

                return isAllowed
                    ? GuardrailDecision.Allow()
                    : GuardrailDecision.Block(
                        $"'{toolName}' sends a request off this machine and approval was not given, so "
                        + "nothing was sent.");
            });
    }

    /// <summary>
    /// Installs every host guardrail onto a runtime, replacing anything already there.
    /// </summary>
    /// <param name="runtime">The runtime a flow will execute on.</param>
    /// <param name="gate">The egress gate governing this run.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <remarks>
    /// One call site, so a future host guardrail is added in one place and reaches every flow run
    /// rather than the subset of call sites somebody remembered to update.
    /// </remarks>
    public static void InstallOn(FlowRuntime runtime, EgressGate gate)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(gate);

        runtime.HostGuardrails.Clear();
        runtime.HostGuardrails.Add(Egress(gate));
    }
}
