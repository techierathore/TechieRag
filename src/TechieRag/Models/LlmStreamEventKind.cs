namespace TechieRag.Models;

/// <summary>Identifies what a single <see cref="LlmStreamEvent"/> carries (REQ-RAG-067 / BRD-110).</summary>
public enum LlmStreamEventKind
{
    /// <summary>An incremental piece of assistant text, in the order the model produced it.</summary>
    TextDelta,

    /// <summary>A tool call the model has decided on: name, complete arguments and id.</summary>
    ToolCall,

    /// <summary>The last event of a stream: token usage, finish reason and model name.</summary>
    Completed
}
