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
    MaxIterationsReached
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
/// </remarks>
public sealed class AgentStep
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
}
