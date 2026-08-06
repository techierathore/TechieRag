namespace TechieRag.Connectors;

/// <summary>
/// What one connector run did (REQ-RAG-032, BRD-65).
/// </summary>
/// <remarks>
/// Four outcomes, kept apart on purpose. "Fetched", "unchanged" and "failed" are three different
/// things that a single count would blur into one number nobody can act on, and
/// <see cref="Sync"/> is what makes the next run cheap.
/// </remarks>
/// <param name="Documents">Items fetched successfully, with their text.</param>
/// <param name="Unchanged">Items skipped because their version matched the previous run. Empty unless <see cref="ConnectorRunOptions.ReportUnchanged"/> is set.</param>
/// <param name="Failures">Items that could not be fetched, each with a reason. Never silently dropped.</param>
/// <param name="Sync">State to persist and hand to the next run. Carries forward versions for items this run skipped.</param>
/// <param name="ReachedLimit">True when a budget in <see cref="ConnectorRunOptions"/> stopped the run before the source was exhausted.</param>
public sealed record ConnectorRunResult(
    IReadOnlyList<ConnectorDocument> Documents,
    IReadOnlyList<ConnectorItem> Unchanged,
    IReadOnlyList<ConnectorItemFailure> Failures,
    ConnectorSyncState Sync,
    bool ReachedLimit = false);
