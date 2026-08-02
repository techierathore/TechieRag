namespace TechieRag.Connectors;

/// <summary>
/// Enumerates documents from a remote source (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// <para><b>Two methods, not one.</b> <see cref="ListAsync"/> answers "what is there" and
/// <see cref="FetchAsync"/> answers "what does it say", and they are split because every source
/// prices them differently — one request lists a repository tree, one request per file downloads it.
/// Collapsing them into a single "give me everything" call would make incremental sync impossible:
/// deciding not to download an item is exactly the saving incremental sync exists to make.</para>
/// <para><b>Failure is per item, by contract.</b> <see cref="FetchAsync"/> throwing removes one item
/// from the run and nothing else — <see cref="ConnectorRunner"/> records a
/// <see cref="ConnectorItemFailure"/> and moves on. Connectors must therefore reserve
/// <see cref="ConnectorException"/> for conditions that make the rest of the run pointless (bad
/// credentials, missing source, exhausted rate limit) and let ordinary per-item problems surface as
/// ordinary exceptions. A run over ten thousand files where one is a corrupt blob is a successful
/// run with one failure, not a failed run.</para>
/// <para><b>Transport belongs behind an interface.</b> Every connector in this library takes its
/// network access as a constructor dependency — an HTTP transport, a mail transport — so the
/// listing, paging, filtering and sync logic that is the actual product can be tested against a fake
/// with no network. That is the same seam <c>IWebContentFetcher</c> provides for the crawler.</para>
/// <para><b>Credentials are inputs, never storage.</b> A connector receives its token or password on
/// its options object and holds it in memory for the run. The library has no secret store and will
/// not grow one: the caller supplies the credential from wherever it already keeps secrets
/// (TechieDesk uses the OS keychain via its own <c>ISecretStore</c>). No connector writes a
/// credential to disk, puts one in a URL, or includes one in an exception message or log line.</para>
/// </remarks>
public interface IDataConnector
{
    /// <summary>Gets a short, stable name for the kind of source this connector reads.</summary>
    /// <remarks>Recorded as <c>SourceType</c> on every ingested document, so it must not change between releases.</remarks>
    /// <value>For example "repository", "confluence" or "email".</value>
    string SourceType { get; }

    /// <summary>Gets a human-facing description of the specific source being read.</summary>
    /// <remarks>Shown in job views and used in failure messages. Must never contain a credential.</remarks>
    /// <value>For example "owner/repo@main" or "imap.example.test/INBOX".</value>
    string SourceName { get; }

    /// <summary>
    /// Gets a value indicating whether a completed listing enumerates every item in the source.
    /// </summary>
    /// <remarks>
    /// <para><b>This is what makes it safe to forget items.</b> After a full walk,
    /// <see cref="ConnectorRunner"/> drops sync-state entries for items it did not see, so a file
    /// deleted at the source stops being tracked and the state cannot grow without bound. That is
    /// only correct when "did not see it" really means "it is gone".</para>
    /// <para>A connector that asks the source for <i>changes only</i> — an IMAP <c>SINCE</c> search,
    /// a wiki queried by last-modified date — sees almost nothing on an incremental run, and
    /// pruning against that listing would throw away the versions that made the run incremental in
    /// the first place. Every later run would then re-fetch the whole source. Such a connector
    /// returns false.</para>
    /// <para>Defaults to true, the safe answer: a connector that declares it wrongly wastes work,
    /// while the opposite default would silently grow state forever for the common case.</para>
    /// </remarks>
    bool ListsEntireSource => true;

    /// <summary>Lists one page of items available in the source.</summary>
    /// <param name="request">Continuation cursor and the previous run's sync state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items in this page and a cursor for the next, or null when the listing is complete.</returns>
    /// <exception cref="ConnectorException">The source could not be listed at all — bad credentials, missing source, or an exhausted rate limit.</exception>
    Task<ConnectorPage> ListAsync(ConnectorListRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetches one listed item's contents as text.</summary>
    /// <param name="item">An item returned by <see cref="ListAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item and its readable text.</returns>
    /// <exception cref="ConnectorException">The whole run cannot continue. Ordinary per-item problems throw other exception types and cost only this item.</exception>
    Task<ConnectorDocument> FetchAsync(ConnectorItem item, CancellationToken cancellationToken = default);
}
