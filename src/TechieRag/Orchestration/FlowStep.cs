using TechieRag.Models;

namespace TechieRag.Orchestration;

/// <summary>
/// One observable step of a flow run, reported through the same
/// <c>IProgress&lt;AgentStep&gt;</c> channel the single-agent loop already uses (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>One trace format, not two.</b> This derives from <see cref="AgentStep"/> so an existing
/// renderer — TechieDesk's <c>AgentTrace</c>, which maps <see cref="AgentStepKind"/> to display
/// rows (REQ-RAG-021 / BRD-85) — keeps working unchanged: it sees the same base properties, in the
/// same order, on the same channel. The additions are all NEW properties and NEW
/// <see cref="AgentStepKind"/> members; nothing existing changed meaning, and the four kinds the
/// single-agent loop emits are emitted identically.</para>
/// <para><b>Inner agent steps are decorated, not replaced.</b> A node's agent turn runs the real
/// <c>AgentLoopRunner</c>, which reports plain <see cref="AgentStep"/> values. The flow re-emits
/// each one as a <see cref="FlowStep"/> carrying the node it happened in, so a tool execution can be
/// attributed to a node without <c>AgentLoopRunner</c> knowing flows exist.</para>
/// </remarks>
public class FlowStep : AgentStep
{
    /// <summary>Gets the identifier of the run this step belongs to.</summary>
    public required string RunId { get; init; }

    /// <summary>Gets the node this step happened in; null for run-level steps.</summary>
    public string? NodeId { get; init; }

    /// <summary>Gets the node's display name; null for run-level steps.</summary>
    public string? NodeName { get; init; }

    /// <summary>Gets what the node does; null for run-level steps.</summary>
    public FlowNodeKind? NodeKind { get; init; }

    /// <summary>Gets the node control left, for routing and handoff steps.</summary>
    public string? FromNodeId { get; init; }

    /// <summary>Gets the node control moved to, for routing and handoff steps.</summary>
    public string? ToNodeId { get; init; }

    /// <summary>Gets the edge taken, for <see cref="AgentStepKind.RouteTaken"/> steps.</summary>
    public string? EdgeId { get; init; }

    /// <summary>Gets the guardrail that refused, for <see cref="AgentStepKind.GuardrailBlocked"/> steps.</summary>
    public string? GuardrailId { get; init; }

    /// <summary>Gets the stage a guardrail refused at, for <see cref="AgentStepKind.GuardrailBlocked"/> steps.</summary>
    public GuardrailStage? GuardrailStage { get; init; }

    /// <summary>
    /// Gets the nesting depth: 0 for the outermost flow, 1 inside an agent-as-tool call, and so on.
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// Re-emits a step reported by the single-agent loop, attributed to the node it ran in.
    /// </summary>
    /// <param name="step">The step reported by <c>AgentLoopRunner</c>.</param>
    /// <param name="runId">The flow run's identifier.</param>
    /// <param name="node">The node whose agent turn produced it.</param>
    /// <param name="depth">The nesting depth.</param>
    /// <returns>The same step, as a flow step.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public static FlowStep FromAgentStep(AgentStep step, string runId, FlowNode node, int depth)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(node);

        return new FlowStep
        {
            RunId = runId,
            Iteration = step.Iteration,
            Kind = step.Kind,
            ToolName = step.ToolName,
            ToolArgumentsJson = step.ToolArgumentsJson,
            Content = step.Content,
            IsSuccess = step.IsSuccess,
            ErrorMessage = step.ErrorMessage,
            NodeId = node.Id,
            NodeName = node.DisplayName,
            NodeKind = node.Kind,
            Depth = depth
        };
    }
}
