using TechieRag.Models;

namespace TechieRag.Orchestration;

/// <summary>How a flow run ended (REQ-RAG-042).</summary>
public enum FlowRunOutcome
{
    /// <summary>A terminal node was reached, or a node had no satisfied outgoing edge.</summary>
    Completed,

    /// <summary>A guardrail refused a node's input or output, so the run stopped there.</summary>
    Blocked,

    /// <summary>
    /// The step budget ran out. The termination guarantee doing its job — not an error in the
    /// engine, but usually a flow that loops.
    /// </summary>
    StepBudgetExhausted,

    /// <summary>The flow could not run or a node failed unrecoverably; see <see cref="FlowRunResult.FailureReason"/>.</summary>
    Failed,

    /// <summary>The caller cancelled the run.</summary>
    Cancelled
}

/// <summary>
/// Everything one flow run produced: its answer, how it got there, and why it stopped
/// (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>The trace is always here.</b> <see cref="Steps"/> is collected whether or not the caller
/// passed an <c>IProgress</c>, because a UI that renders a run after the fact — from a persisted
/// record, or after a page refresh — cannot subscribe retroactively. The progress channel is for
/// live rendering; this is the record.</para>
/// <para><b>A blocked run is not a failed run.</b> <see cref="FlowRunOutcome.Blocked"/> is a
/// successful guardrail doing its job, and it carries the guardrail and reason so a UI can say what
/// stopped and why rather than showing a generic error.</para>
/// </remarks>
public sealed class FlowRunResult
{
    /// <summary>Gets the identifier of this run, matching <see cref="FlowStep.RunId"/> on every step.</summary>
    public required string RunId { get; init; }

    /// <summary>Gets the id of the flow that was run.</summary>
    public required string FlowId { get; init; }

    /// <summary>Gets how the run ended.</summary>
    public required FlowRunOutcome Outcome { get; init; }

    /// <summary>Gets whether the run reached a normal end.</summary>
    public bool IsSuccess => Outcome == FlowRunOutcome.Completed;

    /// <summary>Gets the flow's output — the last node's output, or the terminal node's.</summary>
    public string? Output { get; init; }

    /// <summary>Gets the terminal node's <see cref="FlowNode.TerminalStatus"/>, when one was reached.</summary>
    public string? TerminalStatus { get; init; }

    /// <summary>Gets the id of the node the run stopped at.</summary>
    public string? LastNodeId { get; init; }

    /// <summary>Gets the full trace, in execution order, including every inner agent step.</summary>
    public required IReadOnlyList<FlowStep> Steps { get; init; }

    /// <summary>Gets the ids of the nodes that executed, in order, with repeats when a cycle ran.</summary>
    public required IReadOnlyList<string> VisitedNodeIds { get; init; }

    /// <summary>Gets the flow variables as they stood when the run ended.</summary>
    public required IReadOnlyDictionary<string, string> Variables { get; init; }

    /// <summary>Gets how many nodes were executed, against <see cref="FlowDefinition.MaxSteps"/>.</summary>
    public required int StepsExecuted { get; init; }

    /// <summary>Gets the guardrail that stopped the run, for <see cref="FlowRunOutcome.Blocked"/>.</summary>
    public string? BlockedByGuardrailId { get; init; }

    /// <summary>Gets why the run was blocked, for <see cref="FlowRunOutcome.Blocked"/>.</summary>
    public string? BlockReason { get; init; }

    /// <summary>
    /// Gets the localizable form of <see cref="BlockReason"/> — a stable code plus its arguments —
    /// or null when the refusing guardrail supplied only English (REQ-RAG-050).
    /// </summary>
    /// <remarks>
    /// <see cref="BlockReason"/> is painted verbatim in an alert that says why a run stopped, so an
    /// English clause lands inside an otherwise translated screen. A consumer renders this code when
    /// it recognises it and falls back to <see cref="BlockReason"/> when it does not.
    /// </remarks>
    public FlowMessage? BlockMessage { get; init; }

    /// <summary>Gets why the run failed, for <see cref="FlowRunOutcome.Failed"/>.</summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Gets the localizable form of <see cref="FailureReason"/> — a stable code plus its arguments
    /// (REQ-RAG-050).
    /// </summary>
    /// <remarks>
    /// For a flow refused by <see cref="FlowValidator"/> the argument is the joined English issue
    /// messages, and the better rendering is available: <see cref="ValidationIssues"/> carries every
    /// issue's own <see cref="FlowValidationIssue.Code"/>, so a consumer can translate each one and
    /// join them itself rather than using the argument at all.
    /// </remarks>
    public FlowMessage? FailureMessage { get; init; }

    /// <summary>
    /// Gets the validation issues that stopped the run before it started. Empty unless the flow was
    /// rejected by <see cref="FlowValidator"/>.
    /// </summary>
    public IReadOnlyList<FlowValidationIssue> ValidationIssues { get; init; } = [];

    /// <summary>Gets the tokens every agent turn in the run consumed in total.</summary>
    public required TokenUsage Usage { get; init; }
}
