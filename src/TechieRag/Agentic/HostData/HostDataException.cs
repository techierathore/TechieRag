using TechieRag.Orchestration;

namespace TechieRag.Agentic.HostData;

/// <summary>Thrown by <see cref="HostDataSkill.UndoAsync"/> when a change cannot be reversed (REQ-RAG-154 / BRD-212).</summary>
public sealed class HostDataException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="HostDataException"/> class.</summary>
    /// <param name="message">The localizable message; its <see cref="FlowMessage.Text"/> is the exception text.</param>
    public HostDataException(FlowMessage message)
        : base(message?.Text)
    {
        ArgumentNullException.ThrowIfNull(message);
        Detail = message;
    }

    /// <summary>Gets the stable code, one of <see cref="HostDataMessageCodes"/>.</summary>
    public string Code => Detail.Code;

    /// <summary>Gets the localizable form of the message.</summary>
    public FlowMessage Detail { get; }
}
