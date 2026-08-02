namespace TechieRag.Connectors.Email;

/// <summary>
/// What to ingest from a mailbox (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>The defaults here are a privacy position, not a preference.</b> A mailbox is the
/// highest-sensitivity source this product reads, and everything ingested becomes answerable to
/// anyone who can query the workspace. So the connector ingests one folder, no spam, nothing the
/// account sent, and no attachments unless asked. Widening any of those is an explicit act by
/// someone who has thought about what ends up in the index.</para>
/// <para><b>Credentials are inputs.</b> <see cref="Password"/> is supplied by the caller from
/// wherever it already stores secrets — TechieDesk reads it from the OS keychain through its own
/// <c>ISecretStore</c> — and lives in memory for the run only. TechieRag has no secret store and
/// never writes this value to disk or includes it in a log line or an exception message. Do not
/// persist a populated instance of this class.</para>
/// </remarks>
public sealed class EmailConnectorOptions
{
    /// <summary>Gets or sets the folders to ingest.</summary>
    /// <remarks>
    /// Empty means every folder the server lists, minus the excluded ones below — which on a real
    /// account includes Trash, Drafts and everything ever archived. INBOX alone is the default
    /// because it is the answer someone would give if asked, and "all mail" is not.
    /// </remarks>
    public IList<string> Folders { get; set; } = ["INBOX"];

    /// <summary>Gets or sets the earliest message date to ingest.</summary>
    /// <remarks>Day granularity: IMAP's own <c>SINCE</c> has no finer resolution.</remarks>
    public DateTimeOffset? SinceUtc { get; set; }

    /// <summary>Gets or sets a substring the sender must contain.</summary>
    public string? SenderContains { get; set; }

    /// <summary>Gets or sets a substring the subject must contain.</summary>
    public string? SubjectContains { get; set; }

    /// <summary>Gets or sets the account's own address, used to recognise messages it sent.</summary>
    /// <remarks>Required for <see cref="IncludeSentByMe"/> to mean anything; without it, sent mail can only be excluded by not selecting the Sent folder.</remarks>
    public string? AccountAddress { get; set; }

    /// <summary>Gets or sets whether messages the account sent are ingested.</summary>
    /// <remarks>
    /// Off by default and named as an acceptance criterion in BRD-135. Sent mail doubles the size of
    /// every thread in the index with the half the user is least likely to be searching for.
    /// </remarks>
    public bool IncludeSentByMe { get; set; }

    /// <summary>Gets or sets whether junk and spam folders are ingested.</summary>
    /// <remarks>
    /// Off by default and named as an acceptance criterion in BRD-135. Spam is adversarial text
    /// written to be persuasive, and a RAG index that answers from it is a phishing amplifier.
    /// </remarks>
    public bool IncludeSpam { get; set; }

    /// <summary>Gets or sets whether attachment text is appended to the message.</summary>
    /// <remarks>Off by default: attachments are where the confidential documents are.</remarks>
    public bool IncludeAttachments { get; set; }

    /// <summary>Gets or sets the attachment file extensions whose text is extracted.</summary>
    /// <remarks>
    /// An allow-list rather than a deny-list. A deny-list would hand every unrecognised format to a
    /// processor and hope, and the set of things people attach is unbounded.
    /// </remarks>
    public IList<string> AttachmentExtensions { get; set; } =
        [".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".md", ".csv"];

    /// <summary>Gets or sets the largest attachment whose text is extracted, in bytes.</summary>
    public long MaxAttachmentBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>Gets or sets whether quoted reply history is removed from the body.</summary>
    /// <remarks>On by default. See <see cref="ReplyTrimmer"/> for why this matters to retrieval quality rather than to tidiness.</remarks>
    public bool StripQuotedReplies { get; set; } = true;

    /// <summary>Gets or sets whether a trailing signature block is removed from the body.</summary>
    public bool StripSignatures { get; set; } = true;

    /// <summary>Gets or sets how many messages to list per call.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Folder names that are junk or spam under any common server's naming.</summary>
    /// <remarks>Matched case-insensitively against the last segment of a folder path.</remarks>
    public static IReadOnlyList<string> SpamFolderNames { get; } = ["spam", "junk", "junk e-mail", "bulk mail"];

    /// <summary>Determines whether a folder is a junk or spam folder.</summary>
    /// <param name="folder">A folder name, possibly hierarchical.</param>
    /// <returns>True when the folder's last segment names a junk folder.</returns>
    public static bool IsSpamFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        // Servers differ on the hierarchy separator, so both are treated as one.
        var leaf = folder.Replace('\\', '/');
        var slash = leaf.LastIndexOf('/');
        leaf = (slash >= 0 ? leaf[(slash + 1)..] : leaf).Trim();

        foreach (var name in SpamFolderNames)
        {
            if (string.Equals(leaf, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
