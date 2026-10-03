namespace TechieRag.Connectors.Email;

/// <summary>What happened to one message (REQ-RAG-110 / BRD-169).</summary>
/// <param name="Action">The action this result answers.</param>
/// <param name="Outcome">Done, skipped or failed.</param>
/// <param name="Strategy">How the move was carried out, for a move or a Trash; otherwise <see cref="MailMoveStrategy.None"/>.</param>
/// <param name="TargetFolder">The folder the message was moved to (for Trash, the resolved Trash folder), or null.</param>
/// <param name="Code">A <see cref="MailActionCodes"/> value when the outcome is not <see cref="MailActionOutcome.Done"/>; switch on this, never on <paramref name="Reason"/>.</param>
/// <param name="Reason">An English explanation for logs and operators, or null.</param>
public sealed record MailActionResult(
    MailAction Action,
    MailActionOutcome Outcome,
    MailMoveStrategy Strategy = MailMoveStrategy.None,
    string? TargetFolder = null,
    string? Code = null,
    string? Reason = null);
