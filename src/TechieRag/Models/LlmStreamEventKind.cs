namespace TechieRag.Models;

/// <summary>Identifies what a single <see cref="LlmStreamEvent"/> carries (REQ-RAG-067 / BRD-110).</summary>
public enum LlmStreamEventKind
{
    /// <summary>An incremental piece of assistant text, in the order the model produced it.</summary>
    TextDelta,

    /// <summary>A tool call the model has decided on: name, complete arguments and id.</summary>
    ToolCall,

    /// <summary>The last event of a stream: token usage, finish reason and model name.</summary>
    Completed,

    /// <summary>An incremental piece of the model's reasoning ("thinking") text, kept apart from the
    /// answer so a host can show or hide it (REQ-RAG-137, Chatur TR-RAG-008).</summary>
    /// <remarks>Added last so the existing members keep their numeric values.</remarks>
    Thinking
}
