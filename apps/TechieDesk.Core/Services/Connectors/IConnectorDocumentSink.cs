using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// What became of one fetched item (REQ-FN-020, BRD-65).
/// </summary>
/// <param name="WasIngested">Whether the item is now searchable.</param>
/// <param name="DocumentId">The catalogue id, when it was ingested.</param>
/// <param name="Reason">
/// Why, in operator terms — for a skip, why it was not ingested; for an ingest, a short note such as
/// the workspace it landed in. Never contains a credential. Carried as codes and arguments because
/// it is written to <c>ScheduleRunItem.Reason</c> and read back long afterwards (REQ-UI-056).
/// </param>
/// <remarks>
/// A skip carries a reason as a matter of type, not of discipline. BRD-65's failure mode is a run
/// that reports "47 ingested" while 12 items vanished, and the cheapest defence against it is making
/// "not ingested, no reason given" impossible to express.
/// </remarks>
public sealed record ConnectorIngestOutcome(bool WasIngested, string? DocumentId, JobMessage Reason)
{
    /// <summary>Creates the outcome for an item that is now in the catalogue.</summary>
    /// <param name="documentId">The catalogue id.</param>
    /// <param name="reason">A short note about where it landed.</param>
    /// <returns>The ingested outcome.</returns>
    public static ConnectorIngestOutcome Ingested(string documentId, JobMessage reason) =>
        new(true, documentId, reason);

    /// <summary>Creates the outcome for an item that was read but not ingested.</summary>
    /// <param name="reason">Why it was not ingested, in terms an operator can act on.</param>
    /// <returns>The skipped outcome.</returns>
    public static ConnectorIngestOutcome Skipped(JobMessage reason) => new(false, null, reason);
}

/// <summary>
/// Where a connector run puts each document, one at a time, as it is fetched (REQ-FN-020).
/// </summary>
/// <remarks>
/// <para><b>Per item, not per run — and that is the whole point.</b> The library's
/// <c>IngestConnectorAsync</c> collects every document and ingests them after the walk, which means a
/// run cancelled at minute nine of ten ingests nothing at all. Handing each document over as it
/// arrives is what makes "cancelling keeps what was already ingested" true rather than aspirational,
/// and it is also what lets the progress bar count ingested documents instead of downloads.</para>
/// <para>Implementations must not throw for an item they simply cannot use: an empty or unreadable
/// item is <see cref="ConnectorIngestOutcome.Skipped"/> with a reason. Throwing is reserved for a
/// sink that is genuinely broken, and costs that one item.</para>
/// </remarks>
public interface IConnectorDocumentSink
{
    /// <summary>Ingests one fetched document.</summary>
    /// <param name="connector">The connector it came from, for source attribution on the document.</param>
    /// <param name="document">The item and its text.</param>
    /// <param name="payload">The run's payload, carrying the workspace and pinning choice.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether it was ingested, and the reason either way.</returns>
    Task<ConnectorIngestOutcome> IngestAsync(
        IDataConnector connector,
        ConnectorDocument document,
        ConnectorJobPayload payload,
        CancellationToken cancellationToken);
}
