namespace TechieRag.Models;

/// <summary>
/// One event of <see cref="Services.AgentLoopRunner.RunStreamAsync"/>: assistant text as it arrives,
/// each tool call and its result, and the final response (REQ-RAG-068 / BRD-111).
/// </summary>
/// <remarks>
/// The trace (<see cref="AgentStep"/> on <c>IProgress&lt;AgentStep&gt;</c>) is still reported exactly as
/// the non-streaming run reports it; this stream is the rendering channel, the trace is the audit channel.
/// </remarks>
public sealed class AgentStreamEvent
{
    /// <summary>Gets what this event carries.</summary>
    public required AgentStreamEventKind Kind { get; init; }

    /// <summary>Gets the 1-based loop iteration (model round-trip) the event belongs to.</summary>
    public required int Iteration { get; init; }

    /// <summary>Gets the text fragment for <see cref="AgentStreamEventKind.TextDelta"/>; null otherwise.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the tool call for <see cref="AgentStreamEventKind.ToolCallRequested"/> and
    /// <see cref="AgentStreamEventKind.ToolExecuted"/>; null otherwise.</summary>
    public ToolCall? ToolCall { get; init; }

    /// <summary>Gets the tool result for <see cref="AgentStreamEventKind.ToolExecuted"/>; null otherwise.</summary>
    public ToolResult? ToolResult { get; init; }

    /// <summary>Gets the final response for <see cref="AgentStreamEventKind.Completed"/>; null otherwise.</summary>
    /// <remarks><see cref="LlmResponse.Content"/> is the text of the last iteration, the answer;
    /// <see cref="LlmResponse.Usage"/> is summed over every model call of the run.</remarks>
    public LlmResponse? Response { get; init; }

    /// <summary>Gets whether the run ended because it hit its iteration limit, on <see cref="AgentStreamEventKind.Completed"/>.</summary>
    public bool MaxIterationsReached { get; init; }
}
