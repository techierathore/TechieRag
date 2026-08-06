namespace TechieRag.Orchestration;

/// <summary>
/// How much of the sending agent's context crosses a handoff boundary (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>This is the token-cost and correctness dial.</b> Handing over a whole transcript is the
/// expensive default that most frameworks pick and the one that lets a tool result from agent A
/// silently become part of agent B's reasoning context. TechieRag defaults to the narrow mode and
/// makes the wide one an explicit, named choice.</para>
/// </remarks>
public enum HandoffContextMode
{
    /// <summary>
    /// Only the sending node's final answer text crosses. The receiving agent starts a fresh
    /// conversation from its own system prompt. This is the default.
    /// </summary>
    LastOutputOnly,

    /// <summary>
    /// The flow's original input and the sending node's final answer both cross, so the receiver
    /// can see what was originally asked as well as what the previous agent concluded.
    /// </summary>
    OriginalInputAndLastOutput,

    /// <summary>
    /// Every message of the sending agent's conversation — system prompt, tool calls and tool
    /// results included — is prepended to the receiving agent's conversation. The expensive mode;
    /// choose it only when the receiver genuinely needs the working, not the conclusion.
    /// </summary>
    FullTranscript
}

/// <summary>
/// Declares what a <see cref="FlowNodeKind.Handoff"/> node transfers, and to whom (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>The contract, stated once.</b> What crosses a handoff is: (1) the text selected by
/// <see cref="ContextMode"/>; (2) the values of the flow variables named in
/// <see cref="CarryVariables"/>, and no others; (3) the <see cref="Reason"/> string, rendered to the
/// receiver as a system note naming the sending node.</para>
/// <para><b>What never crosses implicitly:</b> the sending agent's system prompt, its tool
/// definitions, its tool results, and any flow variable not named in <see cref="CarryVariables"/>.
/// The allowlist is deliberate — a denylist would let a variable added later start leaking into
/// every downstream agent's context the moment it was introduced.</para>
/// <para><b>Flow state is not agent context.</b> Variables stay in the run's own state regardless of
/// this list; <see cref="CarryVariables"/> only decides which of them the receiving MODEL is shown.
/// A later condition can still branch on a variable that was never handed to any agent.</para>
/// </remarks>
public sealed class FlowHandoff
{
    /// <summary>Gets or sets the id of the node receiving control. Must be an agent node.</summary>
    public required string TargetNodeId { get; set; }

    /// <summary>Gets or sets how much of the sending agent's context crosses. Defaults to the narrow mode.</summary>
    public HandoffContextMode ContextMode { get; set; } = HandoffContextMode.LastOutputOnly;

    /// <summary>
    /// Gets or sets the names of the flow variables whose values are shown to the receiving agent.
    /// An allowlist: a variable absent from this list is never rendered into the receiver's context.
    /// </summary>
    public List<string> CarryVariables { get; set; } = [];

    /// <summary>
    /// Gets or sets a one-line explanation of why control is being transferred, shown to the
    /// receiving agent as part of the handoff note. Null omits the explanation but not the note.
    /// </summary>
    public string? Reason { get; set; }
}
