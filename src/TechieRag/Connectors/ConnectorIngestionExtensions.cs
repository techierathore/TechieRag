namespace TechieRag.Connectors;

/// <summary>
/// Ingests a data connector's documents into a TechieRag instance (REQ-RAG-032, BRD-113/63/64/135).
/// </summary>
/// <remarks>
/// Extension methods over <see cref="ITechieRag"/> rather than new members on it, for the same
/// reason web ingestion is: the core interface is implemented by consumers and shipped in a
/// published package, so adding members breaks every implementer. Connector ingestion is strictly a
/// composition of run + <c>IngestTextAsync</c> and introduces no new storage or embedding behaviour,
/// so it has no claim on the core contract.
/// </remarks>
public static class ConnectorIngestionExtensions
{
    /// <summary>Runs a connector and ingests each document as it is fetched (REQ-RAG-032 / BRD-113, REQ-RAG-083 / BRD-127).</summary>
    /// <param name="rag">The RAG instance.</param>
    /// <param name="connector">The connector to run.</param>
    /// <param name="previousSync">State from the previous run, or null for a first, full run.</param>
    /// <param name="options">Run bounds; defaults are conservative.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was ingested, what was skipped and why, and the state for the next run.</returns>
    /// <exception cref="ConnectorException">The source could not be read at all, or too many items failed in a row.</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled; every document fetched before that is already ingested.
    /// It is a <see cref="ConnectorRunCanceledException"/> whose <see cref="ConnectorRunCanceledException.PartialIngestion"/>
    /// holds the ids ingested, the skipped items and the sync state to resume from (REQ-RAG-117).</exception>
    /// <remarks>
    /// <para><b>Re-sync replaces (REQ-RAG-120 / BRD-179):</b> each document is ingested with
    /// <see cref="ConnectorDocumentKey"/> as its caller key, so a changed item replaces the copy an
    /// earlier run stored instead of adding a second one.</para>
    /// <para><b>Partial runs (REQ-RAG-117 / BRD-177):</b> a <see cref="ConnectorException"/> that ends
    /// the run carries the same partial outcome on <see cref="ConnectorException.PartialIngestion"/>.</para>
    /// </remarks>
    public static async Task<ConnectorIngestionResult> IngestConnectorAsync(
        this ITechieRag rag,
        IDataConnector connector,
        ConnectorSyncState? previousSync = null,
        ConnectorRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rag);
        ArgumentNullException.ThrowIfNull(connector);

        var ingested = new List<string>();
        var skipped = new List<ConnectorItemFailure>();

        // Each document is ingested the moment the runner fetches it (REQ-RAG-083 / BRD-127), so a
        // run cancelled or failed part-way keeps everything it had already fetched. Ingesting after
        // the walk lost every fetched document on cancellation (TR-RAG-022).
        ConnectorRunResult run;
        try
        {
            run = await new ConnectorRunner()
                .RunAsync(
                    connector,
                    previousSync,
                    options,
                    async (document, token) =>
                    {
                        // An item whose text is empty is a binary file, an empty page or a message with
                        // nothing but an image in it. Ingesting it would add a document that can never be
                        // retrieved and would make the ingested count a lie about what is searchable. Its
                        // version stays recorded: it was read successfully and genuinely has no text.
                        if (string.IsNullOrWhiteSpace(document.Text))
                        {
                            skipped.Add(new ConnectorItemFailure(
                                document.Item.Id, document.Item.Name, "The item held no readable text.")
                            {
                                Outcome = ConnectorItemOutcome.Skipped,
                            });
                            return;
                        }

                        ingested.Add(await rag.IngestTextAsync(
                            document.Text,
                            document.Item.Name,
                            ConnectorDocumentKey(connector, document.Item),
                            BuildMetadata(connector, document.Item),
                            token).ConfigureAwait(false));
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ConnectorRunCanceledException ex)
        {
            throw new ConnectorRunCanceledException(ex.PartialResult, ex, ex.CancellationToken)
            {
                PartialIngestion = Partial(ingested, skipped, ex.PartialResult),
            };
        }
        catch (ConnectorException ex) when (ex.PartialResult is not null)
        {
            ex.PartialIngestion = Partial(ingested, skipped, ex.PartialResult);
            throw;
        }

        skipped.InsertRange(0, run.Failures);

        return new ConnectorIngestionResult(ingested, skipped, run.Sync, run.ReachedLimit)
        {
            LimitCode = run.LimitCode,
        };
    }

    /// <summary>Snapshots an interrupted ingestion: what was ingested and skipped, and the run's partial sync state.</summary>
    /// <param name="ingested">Document ids ingested so far.</param>
    /// <param name="skipped">Items skipped by ingestion so far.</param>
    /// <param name="run">The runner's partial result.</param>
    /// <returns>The partial ingestion result.</returns>
    private static ConnectorIngestionResult Partial(
        List<string> ingested,
        List<ConnectorItemFailure> skipped,
        ConnectorRunResult run) =>
        new([.. ingested], [.. run.Failures, .. skipped], run.Sync);

    /// <summary>
    /// Builds the caller key connector ingestion stores an item's document under (REQ-RAG-120 / BRD-179).
    /// </summary>
    /// <param name="connector">The connector the item came from.</param>
    /// <param name="item">The item.</param>
    /// <returns><c>connector:&lt;source type&gt;:&lt;source name&gt;:&lt;item id&gt;</c>.</returns>
    /// <remarks>
    /// The item id is the identity; the source type and name are prefixed because two connectors can
    /// both have an item called <c>INBOX/1</c> or <c>README.md</c>, and one must not replace the other.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connector"/> or <paramref name="item"/> is null.</exception>
    public static string ConnectorDocumentKey(IDataConnector connector, ConnectorItem item)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(item);
        return $"connector:{connector.SourceType}:{connector.SourceName}:{item.Id}";
    }

    /// <summary>
    /// Builds the document metadata connector ingestion stores for one item (REQ-RAG-118 / BRD-178).
    /// </summary>
    /// <param name="connector">The connector the item came from.</param>
    /// <param name="item">The item.</param>
    /// <returns>
    /// <c>SourceType</c>, <c>SourceName</c>, <c>SourceUrl</c>, <c>ItemId</c>, <c>IngestedAtUtc</c>, plus
    /// <c>Version</c>, <c>ModifiedUtc</c> and <c>FileSize</c> when the item has them, then the item's own
    /// metadata — which never overwrites the keys above.
    /// </returns>
    /// <remarks>
    /// Public so a host that ingests connector documents its own way (a workspace, a custom pipeline)
    /// stores exactly the metadata <see cref="IngestConnectorAsync"/> would, and citations read the same.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connector"/> or <paramref name="item"/> is null.</exception>
    public static Dictionary<string, object> BuildMetadata(IDataConnector connector, ConnectorItem item)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(item);

        var metadata = new Dictionary<string, object>
        {
            ["SourceType"] = connector.SourceType,
            ["SourceName"] = connector.SourceName,
            ["SourceUrl"] = item.SourceUrl,
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

        // The source's OWN reported size, when it has one. Better than the default
        // IngestTextAsync computes for itself — that is the byte count of the extracted text, which
        // for a source file with markup or encoding overhead is not the artefact's size. Recorded
        // here so a connector document's library row reports what the source says it is.
        if (item.SizeBytes is { } sizeBytes && sizeBytes > 0)
        {
            metadata[Models.DocumentMetadataKeys.FileSize] = sizeBytes;
        }

        if (item.Metadata is null)
        {
            return metadata;
        }

        foreach (var pair in item.Metadata)
        {
            // Source-specific extras never overwrite the framework's own keys: a connector that
            // happened to emit "SourceType" would otherwise corrupt the field citations depend on.
            if (!metadata.ContainsKey(pair.Key))
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        return metadata;
    }
}

/// <summary>What a connector ingestion did (REQ-RAG-032, BRD-65).</summary>
/// <remarks>Also handed back part-way, on <see cref="ConnectorRunCanceledException.PartialIngestion"/> and
/// <see cref="ConnectorException.PartialIngestion"/>, when a run is cancelled or fails (REQ-RAG-117).</remarks>
/// <param name="DocumentIds">Ids of the documents ingested.</param>
/// <param name="Skipped">Items not ingested, each with a reason. Never silently dropped.</param>
/// <param name="Sync">State to persist and hand to the next run.</param>
/// <param name="ReachedLimit">True when a run budget stopped the walk before the source was exhausted.</param>
public sealed record ConnectorIngestionResult(
    IReadOnlyList<string> DocumentIds,
    IReadOnlyList<ConnectorItemFailure> Skipped,
    ConnectorSyncState Sync,
    bool ReachedLimit = false)
{
    /// <summary>
    /// Gets the stable code of the budget that stopped the run, from <see cref="ConnectorErrorCodes"/>;
    /// null when <see cref="ReachedLimit"/> is false (REQ-RAG-082).
    /// </summary>
    public string? LimitCode { get; init; }
}
