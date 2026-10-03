namespace TechieRag.Connectors.Email;

/// <summary>What a dry run says would happen to one message (REQ-RAG-111 / BRD-170).</summary>
/// <param name="Action">The action this plan answers.</param>
/// <param name="Outcome">The outcome the action would have: <see cref="MailActionOutcome.Done"/> when it would be carried out.</param>
/// <param name="Strategy">How a move or Trash would be carried out on this server.</param>
/// <param name="TargetFolder">The destination folder (for Trash, the resolved Trash folder), or null.</param>
/// <param name="Commands">The IMAP commands that would be sent for this message, without tags; empty when nothing would be sent.</param>
/// <param name="Code">A <see cref="MailActionCodes"/> value when the outcome would not be <see cref="MailActionOutcome.Done"/>.</param>
/// <param name="Reason">An English explanation for logs and operators, or null.</param>
/// <remarks>
/// The plan is made against the live mailbox (capabilities, folders, UIDVALIDITY, which messages
/// exist) over a read-only session, so it predicts what <see cref="ImapMailActions.ApplyAsync"/> would do
/// at that moment. A server can still refuse a command at apply time; that refusal is reported then.
/// </remarks>
public sealed record MailActionPlan(
    MailAction Action,
    MailActionOutcome Outcome,
    MailMoveStrategy Strategy,
    string? TargetFolder,
    IReadOnlyList<string> Commands,
    string? Code = null,
    string? Reason = null);
