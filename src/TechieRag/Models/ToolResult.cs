using TechieRag.Orchestration;

namespace TechieRag.Models;

/// <summary>Represents the result of executing a tool call.</summary>
public class ToolResult
{
    /// <summary>Gets or sets the ID of the tool call this result responds to.</summary>
    public required string ToolCallId { get; set; }

    /// <summary>Gets or sets the result content.</summary>
    public required string Content { get; set; }

    /// <summary>Gets or sets whether the tool execution was successful.</summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>Gets or sets an error message if the tool execution failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the localizable form of <see cref="ErrorMessage"/> — a stable code plus its
    /// arguments — or null when the handler reported the failure in English only (REQ-RAG-050).
    /// </summary>
    /// <remarks>
    /// <para><b><see cref="Content"/> and this property have different readers, and that is the whole
    /// point.</b> <see cref="Content"/> is read by the MODEL so it can adapt and finish its turn, and
    /// stays a finished English sentence. <see cref="ErrorMessage"/> is what a host paints in a trace
    /// row, so it is what a PERSON reads — and it is the one that has to be translatable.</para>
    /// <para>Flows through to <see cref="AgentStep.FailureMessage"/>, which is what a renderer
    /// actually sees.</para>
    /// </remarks>
    public FlowMessage? Message { get; set; }
}
