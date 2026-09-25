namespace TechieRag.Models;

/// <summary>Identifies what a single <see cref="AgentStreamEvent"/> carries (REQ-RAG-068 / BRD-111).</summary>
public enum AgentStreamEventKind
{
    /// <summary>Assistant text as it arrives from the model, in any iteration.</summary>
    TextDelta,

    /// <summary>The model decided on a tool call; it is about to be executed.</summary>
    ToolCallRequested,

    /// <summary>A tool call was executed through the tool handler and produced a result.</summary>
    ToolExecuted,

    /// <summary>The run ended; carries the final response. Always the last event.</summary>
    Completed
}
