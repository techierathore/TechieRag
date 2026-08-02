using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// One kind of source the installed build can read, for the connector hub's "add a connector" list.
/// </summary>
/// <param name="ConnectorType">The stable key stored on <see cref="ConnectorJobPayload.ConnectorType"/>.</param>
/// <param name="DisplayNameKey">Resource key for the human-facing name of this kind of source.</param>
/// <param name="DescriptionKey">Resource key for the one line describing what it reads.</param>
/// <remarks>
/// REQ-UI-051 / BRD-91: <paramref name="ConnectorType"/> is WIRE vocabulary — it is the library
/// connector's own <c>SourceType</c>, it is written into the <c>Connector</c> table, into
/// <see cref="ConnectorJobPayload.ConnectorType"/> and onto every ingested document's metadata — so it
/// stays culture-invariant. The two display members are resource KEYS, so a descriptor cannot carry
/// English to a screen; the connector hub and the connector editor resolve them.
/// </remarks>
public sealed record ConnectorTypeDescriptor(
    string ConnectorType, string DisplayNameKey, string DescriptionKey);

/// <summary>
/// A connector made ready to run, with whatever the previous run learned.
/// </summary>
/// <param name="Connector">The live connector, credentials already resolved, ready to list and fetch.</param>
/// <param name="PreviousSync">What the previous run saw, or <see langword="null"/> for a first, full run.</param>
public sealed record ResolvedConnector(IDataConnector Connector, ConnectorSyncState? PreviousSync);

/// <summary>
/// The seam between connector jobs and the connectors themselves (REQ-FN-020 ↔ REQ-RAG-032).
/// </summary>
/// <remarks>
/// <para><b>This is the only thing the job cluster asks of the connector cluster.</b> The job side
/// owns "run it in the background, show progress, record every item and every reason, stop when
/// asked". The connector side owns "what is a repository, where is the token, what did we see last
/// time". They meet here and nowhere else — <see cref="ConnectorJobHandler"/> never names a concrete
/// connector, and no connector ever names a job, a schedule or a run row.</para>
/// <para><b>Sync state comes back through the same seam it went out through.</b> The library is
/// explicit that it does not persist <see cref="ConnectorSyncState"/>; whoever stores a connector's
/// configuration is already the right owner of the state that goes with it, so the store lives behind
/// this interface rather than becoming a second table the job cluster owns.</para>
/// <para><b>Implementations must be safe to resolve inside a background scope.</b> A connector job
/// runs on a thread pool thread with no Blazor circuit, and — when the scheduler helper hosts it —
/// with no window at all.</para>
/// </remarks>
public interface IConnectorResolver
{
    /// <summary>Gets the connector types this build can run.</summary>
    /// <remarks>Empty is a legitimate answer, and the connector hub must render it as "none installed".</remarks>
    IReadOnlyList<ConnectorTypeDescriptor> AvailableTypes { get; }

    /// <summary>Checks a payload against the connector it names, before a schedule is saved.</summary>
    /// <param name="payload">The payload to check.</param>
    /// <returns><see langword="null"/> when the payload is usable, otherwise the reason it is not.</returns>
    /// <remarks>
    /// A <see cref="JobMessage"/> because the same refusal is BOTH shown in the confirm dialog and,
    /// when the run goes ahead anyway on a schedule saved earlier, persisted as the run's failure
    /// reason (REQ-UI-056).
    /// </remarks>
    JobMessage? Validate(ConnectorJobPayload payload);

    /// <summary>Builds the connector this payload names, ready to run.</summary>
    /// <param name="payload">What to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connector and the previous run's sync state.</returns>
    /// <exception cref="ConnectorException">
    /// The connector could not be built — it was deleted, or its credential is gone. An app-authored
    /// refusal throws <see cref="ConnectorSetupException"/>, which carries the reason as codes.
    /// </exception>
    Task<ResolvedConnector> ResolveAsync(ConnectorJobPayload payload, CancellationToken cancellationToken);

    /// <summary>Persists what this run saw, for the next run to skip.</summary>
    /// <param name="payload">The run's payload, naming the connector the state belongs to.</param>
    /// <param name="sync">The state to keep.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the state is stored.</returns>
    /// <remarks>
    /// Called on every terminal path that ingested anything — including a cancelled run and a run that
    /// died on a source-level failure. Saving only on a clean finish would make a user who pressed
    /// Stop re-download everything the run had already ingested, which is the opposite of what
    /// "cancelling keeps what was already ingested" is supposed to mean.
    /// </remarks>
    Task SaveSyncAsync(ConnectorJobPayload payload, ConnectorSyncState sync, CancellationToken cancellationToken);
}
