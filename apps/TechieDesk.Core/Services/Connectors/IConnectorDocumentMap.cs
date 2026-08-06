namespace TechieDesk.Services.Connectors;

/// <summary>
/// Remembers which catalogue document each connector item currently is, so a re-sync REPLACES a
/// document instead of adding a second copy of it (REQ-RAG-019, REQ-RAG-020).
/// </summary>
/// <remarks>
/// <para><b>Why this exists at all.</b> <c>ITechieRag.IngestTextAsync</c> has no upsert — every call
/// mints a new document id — and nothing in the connector framework mapped a source item to the
/// document it became. So the second sync of a repository whose three files had changed did not
/// update three documents, it added three more: nine catalogue documents became twelve, and a search
/// for anything in those files returned the old text and the new text side by side with no way to
/// tell which was current. That is silent data corruption for every repeatedly-synced source, which
/// is every source a connector exists for.</para>
/// <para><b>Why it is keyed by connector as well as by item.</b> Item ids are only unique within a
/// source: two repositories both have a <c>README.md</c>, and two Confluence spaces both have page
/// ids from the same sequence. Keying on the item alone would make syncing a second repository delete
/// the first one's documents.</para>
/// </remarks>
public interface IConnectorDocumentMap
{
    /// <summary>Finds the document this item currently is.</summary>
    /// <param name="connectorId">The connector that owns the item.</param>
    /// <param name="itemId">The source's identifier for the item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The catalogue id of the document last ingested for this item, or <see langword="null"/>.</returns>
    Task<string?> FindDocumentAsync(
        string connectorId, string itemId, CancellationToken cancellationToken = default);

    /// <summary>Records that this item is now this document.</summary>
    /// <param name="connectorId">The connector that owns the item.</param>
    /// <param name="itemId">The source's identifier for the item.</param>
    /// <param name="documentId">The catalogue id just written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the mapping is stored.</returns>
    Task RecordAsync(
        string connectorId,
        string itemId,
        string documentId,
        CancellationToken cancellationToken = default);
}
