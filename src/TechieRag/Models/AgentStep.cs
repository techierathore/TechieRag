using TechieRag.Orchestration;

namespace TechieRag.Models;

/// <summary>Identifies what kind of event a single <see cref="AgentStep"/> represents.</summary>
public enum AgentStepKind
{
    /// <summary>The LLM requested one or more tool calls in this iteration.</summary>
    ToolCallRequested,

    /// <summary>A single tool was executed and produced a result.</summary>
    ToolExecuted,

    /// <summary>The LLM produced its final text answer (no further tool calls).</summary>
    FinalAnswer,

    /// <summary>The loop hit its iteration limit and a final answer was forced.</summary>
    MaxIterationsReached,

    /// <summary>An orchestrated flow began executing one node (REQ-RAG-042).</summary>
    NodeStarted,

    /// <summary>An orchestrated flow finished one node and recorded its output (REQ-RAG-042).</summary>
    NodeCompleted,

    /// <summary>An orchestrated flow followed one outgoing edge to the next node (REQ-RAG-042).</summary>
    RouteTaken,

    /// <summary>Control passed from one agent to another, carrying the declared context (REQ-RAG-042).</summary>
    HandoffPerformed,

    /// <summary>A guardrail refused a step; nothing it guarded was executed (REQ-RAG-042).</summary>
    GuardrailBlocked,

    /// <summary>The flow's step budget ran out, so execution stopped rather than looping (REQ-RAG-042).</summary>
    StepBudgetExhausted,

    /// <summary>An orchestrated flow reached a terminal node and stopped (REQ-RAG-042).</summary>
    FlowCompleted
}

/// <summary>
/// Describes a single observable step of an <see cref="Services.AgentLoopRunner"/> run,
/// reported to callers via the <c>IProgress&lt;AgentStep&gt;</c> passed to
/// <see cref="Services.AgentLoopRunner.RunAsync"/>.
/// </summary>
/// <remarks>
/// One step is emitted per LLM tool-call request, per individual tool execution, and for the
/// final answer (or when the iteration limit is reached). This lets a UI render an execution
/// trace of what the agent actually did, rather than only the final response.
/// <para><b>Extension point (REQ-RAG-042).</b> This type is deliberately NOT sealed. Multi-agent
/// orchestration reports through the very same <c>IProgress&lt;AgentStep&gt;</c> channel, emitting
/// <c>TechieRag.Orchestration.FlowStep</c> — a subclass that adds node, routing and guardrail
/// identity. An existing single-agent trace renderer therefore keeps working against the base
/// properties without knowing flows exist, and a flow-aware renderer pattern-matches on
/// <c>FlowStep</c> for the extra detail. A second, parallel trace format was rejected precisely
/// because it would fork what a consumer has to render.</para>
/// </remarks>
public class AgentStep
{
    /// <summary>Gets the 1-based agent-loop iteration this step belongs to.</summary>
    public required int Iteration { get; init; }

    /// <summary>Gets the kind of event this step represents.</summary>
    public required AgentStepKind Kind { get; init; }

    /// <summary>Gets the tool name for tool-related steps; null otherwise.</summary>
    public string? ToolName { get; init; }

    /// <summary>Gets the JSON-serialized arguments for a <see cref="AgentStepKind.ToolExecuted"/> step; null otherwise.</summary>
    public string? ToolArgumentsJson { get; init; }

    /// <summary>
    /// Gets the textual payload for the step: the tool result content for
    /// <see cref="AgentStepKind.ToolExecuted"/>, or the answer text for
    /// <see cref="AgentStepKind.FinalAnswer"/> / <see cref="AgentStepKind.MaxIterationsReached"/>.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>Gets whether a tool execution succeeded. Always true for non-tool steps.</summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>Gets the error message when a tool execution failed; null otherwise.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the localizable form of <see cref="ErrorMessage"/> — a stable code plus its arguments —
    /// or null when the failure carried only English (REQ-RAG-050 / REQ-RAG-051).
    /// </summary>
    /// <remarks>
    /// <para><b>It lives on the base step, not on <c>FlowStep</c>, because the row that needed it is
    /// a plain one.</b> A host's trace renderer paints <see cref="ErrorMessage"/> as the detail line
    /// under a failed tool row, and <c>AgentLoopRunner</c> emits that row as an
    /// <see cref="AgentStepKind.ToolExecuted"/> step for EVERY refused call — including the one an
    /// Agent node produces inside a flow, which is re-emitted by <c>FlowStep.FromAgentStep</c>. With
    /// the code slot only on the subclass there was nothing for that copy to carry, so a refusal
    /// that <c>GuardedToolHandler</c> had already coded still reached the screen as English.</para>
    /// <para>Populated from <see cref="ToolResult.Message"/>, which is where a handler publishes the
    /// coded form of its own refusal. Null for a failure a handler reported in English only — a
    /// consumer falls back to <see cref="ErrorMessage"/> and is no worse off than before.</para>
    /// </remarks>
    public FlowMessage? FailureMessage { get; init; }
}
