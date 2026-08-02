using TechieDesk.Services.Scheduling;
using TechieDesk.Services.Web;
using TechieRag;
using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// The default <see cref="IConnectorDocumentSink"/>: ingests into the TechieRag catalogue and links
/// the result into a workspace (REQ-FN-020, BRD-65).
/// </summary>
/// <remarks>
/// <para>The two things the library deliberately does not know about are which workspace a document
/// belongs to and whether it should be pinned, which is exactly the split
/// <see cref="Web.WebIngestionService"/> already makes for the crawler. This class is the connector
/// equivalent, and it reuses the same <see cref="IWorkspaceDocumentLinker"/> rather than growing a
/// second way to put a document in a workspace.</para>
/// <para><b>Empty text is a skip with a reason, never an ingest.</b> An item with no readable text is
/// a binary blob, an image-only message or an empty page; adding it would create a document that can
/// never be retrieved and would make the ingested count a claim about searchability that is false.
/// </para>
/// <para><b>A changed item REPLACES its document; it does not add a second one.</b>
/// <see cref="ITechieRag.IngestTextAsync"/> has no upsert — every call mints a new document id — so
/// without the <see cref="IConnectorDocumentMap"/> below, re-syncing three changed files turned nine
/// catalogue documents into twelve and every search returned the old text alongside the new. The new
/// document is written first and the superseded one deleted immediately after: doing it the other way
/// round would mean an ingestion failure had already destroyed the copy the user could still search.
/// </para>
/// </remarks>
public sealed class RagConnectorDocumentSink : IConnectorDocumentSink
{
    private readonly ITechieRag rag;
    private readonly IWorkspaceDocumentLinker? linker;
    private readonly IConnectorDocumentMap? documents;
    private readonly ILogger<RagConnectorDocumentSink> logger;

    /// <summary>Initializes a new instance of the <see cref="RagConnectorDocumentSink"/> class.</summary>
    /// <param name="rag">The RAG instance documents are ingested into.</param>
    /// <param name="logger">Diagnostics. Item names are logged; item contents and credentials are not.</param>
    /// <param name="linker">
    /// Adds ingested documents to a workspace. Absent in the scheduler helper host, which registers
    /// scheduling and data only — a run there still ingests, and says in each item's reason that the
    /// workspace link was not written.
    /// </param>
    /// <param name="documents">
    /// Remembers which catalogue document each item currently is, so a re-sync replaces rather than
    /// duplicates. Optional so a host with no application database still ingests — such a host simply
    /// cannot supersede, and each item's reason says so rather than pretending.
    /// </param>
    public RagConnectorDocumentSink(
        ITechieRag rag,
        ILogger<RagConnectorDocumentSink> logger,
        IWorkspaceDocumentLinker? linker = null,
        IConnectorDocumentMap? documents = null)
    {
        this.rag = rag ?? throw new ArgumentNullException(nameof(rag));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.linker = linker;
        this.documents = documents;
    }

    /// <inheritdoc />
    public async Task<ConnectorIngestOutcome> IngestAsync(
        IDataConnector connector,
        ConnectorDocument document,
        ConnectorJobPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            return ConnectorIngestOutcome.Skipped(JobMessage.Of("ConnectorItemNoReadableText"));
        }

        var superseded = documents is null || string.IsNullOrWhiteSpace(payload.ConnectorId)
            ? null
            : await documents
                .FindDocumentAsync(payload.ConnectorId, document.Item.Id, cancellationToken)
                .ConfigureAwait(false);

        var documentId = await rag.IngestTextAsync(
            document.Text,
            document.Item.Name,
            BuildMetadata(connector, document.Item),
            cancellationToken).ConfigureAwait(false);

        if (documents is not null && !string.IsNullOrWhiteSpace(payload.ConnectorId))
        {
            await documents
                .RecordAsync(payload.ConnectorId, document.Item.Id, documentId, cancellationToken)
                .ConfigureAwait(false);
        }

        var replaced = await SupersedeAsync(superseded, documentId, document.Item.Name, cancellationToken)
            .ConfigureAwait(false);

        var note = await LinkAsync(documentId, payload, cancellationToken).ConfigureAwait(false);

        // A second SEGMENT rather than a sentence glued onto the first: "where it landed" and "and it
        // replaced the previous copy" are two independent statements, and only one of them is
        // conditional (REQ-UI-056).
        return ConnectorIngestOutcome.Ingested(
            documentId, replaced ? note.Then("ConnectorItemReplacedPreviousCopy") : note);
    }

    /// <summary>Removes the document this item used to be, now that its replacement exists.</summary>
    /// <param name="supersededDocumentId">The previous catalogue id, or <see langword="null"/> for a first ingest.</param>
    /// <param name="documentId">The catalogue id just written.</param>
    /// <param name="itemName">The item's name, for diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when a superseded document was removed.</returns>
    /// <remarks>
    /// A failure to delete the old copy is logged and does NOT fail the item. The replacement is
    /// already in the catalogue and already recorded as this item's document, so the worst case is one
    /// stale document the next sync will not create again — strictly better than reporting an item as
    /// failed when its current text is searchable.
    /// </remarks>
    private async Task<bool> SupersedeAsync(
        string? supersededDocumentId,
        string documentId,
        string itemName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(supersededDocumentId)
            || string.Equals(supersededDocumentId, documentId, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            await rag.DeleteDocumentAsync(supersededDocumentId, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Replaced document {Superseded} with {DocumentId} for connector item {ItemName}",
                supersededDocumentId, documentId, itemName);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "The superseded document {Superseded} for connector item {ItemName} could not be "
                + "removed; the library now holds both copies",
                supersededDocumentId,
                itemName);
            return false;
        }
    }

    /// <summary>Builds the document metadata a citation is later assembled from.</summary>
    /// <param name="connector">The source connector.</param>
    /// <param name="item">The item as listed.</param>
    /// <returns>The metadata dictionary.</returns>
    /// <remarks>
    /// <para>Source-specific extras never overwrite the framework's own keys: a connector that
    /// happened to emit "SourceType" would otherwise corrupt the field citations depend on.</para>
    /// <para><b>"SourcePath" carries the same value as "SourceUrl", deliberately.</b> The app's
    /// default vector store does not round-trip a document's metadata dictionary — <c>SourcePath</c>
    /// is the ONE key it lifts out of chunk metadata onto the catalogue row — so writing only
    /// <c>SourceUrl</c> produced connector documents whose source column was blank and whose
    /// citations pointed nowhere. Web ingestion already duplicates the value for exactly this reason;
    /// this is the same workaround for the same library limitation, logged upstream as TR-RAG-024.
    /// </para>
    /// </remarks>
    private static Dictionary<string, object> BuildMetadata(IDataConnector connector, ConnectorItem item)
    {
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["SourceType"] = connector.SourceType,
            ["SourceName"] = connector.SourceName,
            ["SourceUrl"] = item.SourceUrl,
            ["SourcePath"] = item.SourceUrl,
            ["ItemId"] = item.Id,
            ["IngestedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
        };

        if (item.Version is not null)
        {
            metadata["Version"] = item.Version;
        }

        if (item.ModifiedUtc is { } modified)
        {
            metadata["ModifiedUtc"] = modified.ToString("O");
        }

        foreach (var pair in item.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            if (!metadata.ContainsKey(pair.Key))
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        return metadata;
    }

    /// <summary>Links an ingested document into the run's workspace, when there is one.</summary>
    /// <param name="documentId">The catalogue id just written.</param>
    /// <param name="payload">The run's payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The note recorded against the item, saying where the document actually ended up.</returns>
    private async Task<JobMessage> LinkAsync(
        string documentId, ConnectorJobPayload payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.WorkspaceId))
        {
            return JobMessage.Of("ConnectorItemAddedToLibrary");
        }

        if (linker is null)
        {
            return JobMessage.Of("ConnectorItemAddedNoLinkerHost", payload.WorkspaceId);
        }

        var linked = await linker
            .LinkAsync(payload.WorkspaceId, documentId, payload.Pinned, cancellationToken)
            .ConfigureAwait(false);

        if (linked)
        {
            return JobMessage.Of("ConnectorItemAddedToWorkspace", payload.WorkspaceId);
        }

        // Reported, never swallowed. A document in the library that the workspace cannot see is
        // exactly the "it says it ingested but I cannot find it" report BRD-65 exists to pre-empt.
        logger.LogWarning(
            "Document {DocumentId} was ingested but could not be linked to workspace {WorkspaceId}",
            documentId,
            payload.WorkspaceId);
        return JobMessage.Of("ConnectorItemAddedLinkNotWritten", payload.WorkspaceId);
    }
}
