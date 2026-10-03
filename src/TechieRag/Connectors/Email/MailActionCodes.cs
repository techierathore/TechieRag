namespace TechieRag.Connectors.Email;

/// <summary>
/// The stable codes on <see cref="MailActionResult.Code"/> and <see cref="MailActionPlan.Code"/>
/// (REQ-RAG-110 / BRD-169, REQ-RAG-111 / BRD-170).
/// </summary>
/// <remarks>A host switches on these, never on the English reason. The values never change once shipped; a new case gets a new code.</remarks>
public static class MailActionCodes
{
    /// <summary>A folder, label or UID was empty, not a bare IMAP UID, or carried a control character; nothing was sent for it.</summary>
    public const string InvalidArgument = "MailActionInvalidArgument";

    /// <summary>The message's folder could not be opened.</summary>
    public const string FolderNotFound = "MailActionFolderNotFound";

    /// <summary>The destination folder does not exist on the server. Folders are never created.</summary>
    public const string TargetFolderNotFound = "MailActionTargetFolderNotFound";

    /// <summary>No message with that UID is in the folder (already moved or deleted elsewhere).</summary>
    public const string MessageNotFound = "MailActionMessageNotFound";

    /// <summary>The folder's UIDVALIDITY differs from the one supplied, so the UID may name a different message; nothing was done.</summary>
    public const string UidValidityChanged = "MailActionUidValidityChanged";

    /// <summary>The server advertises neither <c>MOVE</c> nor <c>UIDPLUS</c>, so a move cannot be done without a plain <c>EXPUNGE</c>, which this library never sends.</summary>
    public const string MoveNotSupported = "MailActionMoveNotSupported";

    /// <summary>The server does not offer Gmail's label extension (<c>X-GM-EXT-1</c>). An IMAP keyword is never used in its place.</summary>
    public const string LabelsNotSupported = "MailActionLabelsNotSupported";

    /// <summary>The server lists no <c>\Trash</c> special-use folder and is not Gmail, so there is nowhere recoverable to move the message.</summary>
    public const string TrashFolderNotFound = "MailActionTrashFolderNotFound";

    /// <summary>Skipped: the message is already in the destination folder (or already in Trash).</summary>
    public const string AlreadyInFolder = "MailActionAlreadyInFolder";

    /// <summary>The server refused the command for this message.</summary>
    public const string ServerRefused = "MailActionServerRefused";

    /// <summary>The copy succeeded but removing the original did not, so the message is now in both folders. Nothing was lost.</summary>
    public const string CopiedNotRemoved = "MailActionCopiedNotRemoved";

    /// <summary>The connection failed part-way, so this action was not attempted or its result is unknown.</summary>
    public const string ConnectionLost = "MailActionConnectionLost";
}
