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
    /// <summary>Runs a connector and ingests everything it fetched (REQ-RAG-032 / BRD-113).</summary>
    /// <param name="rag">The RAG instance.</param>
    /// <param name="connector">The connector to run.</param>
    /// <param name="previousSync">State from the previous run, or null for a first, full run.</param>
    /// <param name="options">Run bounds; defaults are conservative.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was ingested, what was skipped and why, and the state for the next run.</returns>
    /// <exception cref="ConnectorException">The source could not be read at all, or too many items failed in a row.</exception>
    public static async Task<ConnectorIngestionResult> IngestConnectorAsync(
        this ITechieRag rag,
        IDataConnector connector,
        ConnectorSyncState? previousSync = null,
        ConnectorRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rag);
        ArgumentNullException.ThrowIfNull(connector);

        var run = await new ConnectorRunner()
            .RunAsync(connector, previousSync, options, cancellationToken)
            .ConfigureAwait(false);

        var ingested = new List<string>();
        var skipped = new List<ConnectorItemFailure>(run.Failures);

        foreach (var document in run.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // An item whose text is empty is a binary file, an empty page or a message with nothing
            // but an image in it. Ingesting it would add a document that can never be retrieved and
            // would make the ingested count a lie about what is searchable.
            if (string.IsNullOrWhiteSpace(document.Text))
            {
                skipped.Add(new ConnectorItemFailure(
                    document.Item.Id, document.Item.Name, "The item held no readable text."));

                // The version stays recorded: the item was read successfully and genuinely has no
                // text, so re-fetching it on every future run would cost the same and find the same.
                continue;
            }

            ingested.Add(await rag.IngestTextAsync(
                document.Text,
                document.Item.Name,
                BuildMetadata(connector, document.Item),
                cancellationToken).ConfigureAwait(false));
        }

        return new ConnectorIngestionResult(ingested, skipped, run.Sync, run.ReachedLimit);
    }

    private static Dictionary<string, object> BuildMetadata(IDataConnector connector, ConnectorItem item)
    {
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
/// <param name="DocumentIds">Ids of the documents ingested.</param>
/// <param name="Skipped">Items not ingested, each with a reason. Never silently dropped.</param>
/// <param name="Sync">State to persist and hand to the next run.</param>
/// <param name="ReachedLimit">True when a run budget stopped the walk before the source was exhausted.</param>
public sealed record ConnectorIngestionResult(
    IReadOnlyList<string> DocumentIds,
    IReadOnlyList<ConnectorItemFailure> Skipped,
    ConnectorSyncState Sync,
    bool ReachedLimit = false);
