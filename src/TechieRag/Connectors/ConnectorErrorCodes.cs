namespace TechieRag.Connectors;

/// <summary>
/// The stable codes a connector run reports when a budget or a safety limit stops it
/// (REQ-RAG-082 / BRD-126).
/// </summary>
/// <remarks>
/// <para>A host switches on these rather than parsing an English message. A budget that ends a
/// run early without failing it is reported on <see cref="ConnectorRunResult.LimitCode"/> (and
/// <see cref="ConnectorIngestionResult.LimitCode"/>) alongside <c>ReachedLimit = true</c>; a limit
/// that makes the run fail is reported on <see cref="ConnectorException.ErrorCode"/>.</para>
/// <para>The values are part of the public contract: they never change once shipped, and a new
/// limit gets a new code.</para>
/// </remarks>
public static class ConnectorErrorCodes
{
    /// <summary>
    /// The run stopped because the fetched text reached <see cref="ConnectorRunOptions.MaxTotalBytes"/>.
    /// Reported on the run result. The sync state is kept; the next run resumes where this one stopped.
    /// </summary>
    public const string RunByteBudgetReached = "ConnectorRunByteBudgetReached";

    /// <summary>
    /// The run stopped because it fetched <see cref="ConnectorRunOptions.MaxItems"/> items.
    /// Reported on the run result.
    /// </summary>
    public const string RunItemLimitReached = "ConnectorRunItemLimitReached";

    /// <summary>
    /// The run stopped because it listed <see cref="ConnectorRunOptions.MaxPages"/> pages.
    /// Reported on the run result.
    /// </summary>
    public const string RunPageLimitReached = "ConnectorRunPageLimitReached";

    /// <summary>
    /// An IMAP server announced a literal larger than what remains of
    /// <see cref="Email.ImapMailboxOptions.MaxMessageBytes"/> for one response, so the connection was
    /// dropped before anything was allocated. Reported on <see cref="ConnectorException.ErrorCode"/>.
    /// </summary>
    public const string ImapLiteralTooLarge = "ConnectorImapLiteralTooLarge";

    /// <summary>
    /// An IMAP server sent a response line longer than the reader's line limit without terminating
    /// it, so the connection was dropped. Reported on <see cref="ConnectorException.ErrorCode"/>.
    /// </summary>
    public const string ImapResponseLineTooLong = "ConnectorImapResponseLineTooLong";
}
