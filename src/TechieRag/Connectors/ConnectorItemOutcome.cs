namespace TechieRag.Connectors;

/// <summary>
/// Whether an item a connector run did not ingest failed or was deliberately skipped
/// (REQ-RAG-118 / BRD-178, Sevak feedback TR-RAG-023).
/// </summary>
/// <remarks>
/// A host switches on this rather than reading the English <see cref="ConnectorItemFailure.Reason"/>.
/// The difference matters to an operator: a failed item is worth retrying, a skipped one is not until
/// something about it (its size, its text) changes.
/// </remarks>
public enum ConnectorItemOutcome
{
    /// <summary>The item could not be read: the fetch threw, or the source reported a problem with it. Retrying may succeed.</summary>
    Failed,

    /// <summary>The item was read or listed fine but deliberately not ingested: over the size limit, or no readable text.</summary>
    Skipped,
}
