namespace TechieRag.Connectors;

/// <summary>
/// Raised when a connector run is cancelled part-way; carries everything the run gathered before
/// it stopped (REQ-RAG-117 / BRD-177, Sevak feedback TR-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>Still an <see cref="OperationCanceledException"/>.</b> A caller's existing
/// <c>catch (OperationCanceledException)</c> keeps working unchanged; a caller that wants to resume
/// catches this type and persists <see cref="PartialResult"/>'s <see cref="ConnectorRunResult.Sync"/>.</para>
/// <para><b>Resuming.</b> The partial sync state records the version of every item finished before
/// the cancellation and keeps the previous run's <see cref="ConnectorSyncState.LastRunUtc"/>, so the
/// next run skips the finished items as unchanged and still reaches the ones this run never did.</para>
/// </remarks>
public sealed class ConnectorRunCanceledException : OperationCanceledException
{
    /// <summary>Initializes a new instance of the <see cref="ConnectorRunCanceledException"/> class.</summary>
    /// <param name="partialResult">What the run gathered before it was cancelled.</param>
    /// <param name="innerException">The cancellation that stopped the run.</param>
    /// <param name="cancellationToken">The token that was cancelled.</param>
    public ConnectorRunCanceledException(
        ConnectorRunResult partialResult,
        Exception? innerException,
        CancellationToken cancellationToken)
        : base("The connector run was cancelled; PartialResult holds the sync state and per-item reasons gathered so far.", innerException, cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(partialResult);
        PartialResult = partialResult;
    }

    /// <summary>
    /// Gets what the run gathered before it was cancelled: per-item reasons, any collected documents,
    /// and the sync state to hand to the next run.
    /// </summary>
    public ConnectorRunResult PartialResult { get; }

    /// <summary>
    /// Gets what connector ingestion had done before the cancellation — the ids of the documents
    /// already ingested, the skipped items and the sync state — when the run came from
    /// <see cref="ConnectorIngestionExtensions.IngestConnectorAsync"/>; otherwise null.
    /// </summary>
    public ConnectorIngestionResult? PartialIngestion { get; init; }
}
