namespace TechieRag.Connectors.Email;

/// <summary>
/// The mail-access seam every email connector goes through (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para>The same role <c>IWebContentFetcher</c> plays for the crawler. What is worth testing in the
/// email connector — folder selection, the scope filters, incremental sync, MIME decoding, reply and
/// signature stripping, per-message failure — must be provable without a mail server, an account, or
/// anybody's real mail. Every email test in this library drives a fake implementation of this
/// interface, and <see cref="MboxMailTransport"/> is a second real implementation that needs no
/// network at all.</para>
/// <para>Searching returns headers and fetching returns bytes, because the whole point of the split
/// is to decide what not to download.</para>
/// </remarks>
public interface IMailTransport
{
    /// <summary>Gets a short description of the mailbox, safe to log and show.</summary>
    /// <remarks>Contains no password or token.</remarks>
    /// <value>For example "imap.example.test" or the file name of an mbox.</value>
    string MailboxName { get; }

    /// <summary>Lists the folders available in the mailbox.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Folder names as the server reports them.</returns>
    /// <exception cref="ConnectorException">The mailbox could not be opened or authenticated.</exception>
    Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns one page of message headers matching the criteria.</summary>
    /// <param name="folder">Folder to search.</param>
    /// <param name="criteria">Scope filters. A transport that cannot express one must apply it locally, never ignore it.</param>
    /// <param name="skip">How many matches to skip.</param>
    /// <param name="take">How many matches to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page of headers and whether more remain.</returns>
    /// <exception cref="ConnectorException">The folder could not be opened or searched.</exception>
    Task<MailSearchPage> SearchAsync(
        string folder,
        MailSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches one message in full, as it exists on the server.</summary>
    /// <param name="header">A header returned by <see cref="SearchAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw RFC 5322 message, headers and body, undecoded.</returns>
    /// <remarks>Raw bytes rather than parsed text: parsing belongs in one place — <see cref="MimeParser"/> — so that every transport decodes identically and the decoder is testable on its own.</remarks>
    Task<byte[]> FetchAsync(MailHeader header, CancellationToken cancellationToken = default);
}
