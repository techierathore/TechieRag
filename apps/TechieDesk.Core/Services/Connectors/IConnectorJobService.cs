using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// Start, watch, stop and read back connector runs (REQ-FN-020, BRD-65).
/// </summary>
/// <remarks>
/// <para><b>This is the whole surface the connector hub screen binds to.</b> It exists so a Razor
/// component never has to know that a connector run is a scheduled job, what a job kind is, or that
/// the payload is JSON — and so that the day a connector run needs a different kind of persistence,
/// no screen changes.</para>
/// <para><b>A run in flight and a finished run are deliberately different types.</b> While it runs, a
/// screen binds <see cref="ActiveRuns"/> — counts and a status line, refreshed on
/// <see cref="Changed"/>. Once it is over, it binds <see cref="ConnectorRunReport"/>, which carries
/// every item and every reason. Trying to serve both from one object would either make the live view
/// carry a growing item list or make the report pretend to be live.</para>
/// </remarks>
public interface IConnectorJobService
{
    /// <summary>Raised whenever any background job starts, reports progress, or finishes.</summary>
    /// <remarks>
    /// Raised on the reporting job's own thread. A Blazor consumer must marshal onto its dispatcher —
    /// <c>InvokeAsync(StateHasChanged)</c> — before touching component state.
    /// </remarks>
    event Action? Changed;

    /// <summary>Gets the connector types this build can run, for the "add a connector" list.</summary>
    IReadOnlyList<ConnectorTypeDescriptor> AvailableTypes { get; }

    /// <summary>Gets a live snapshot of every connector run currently in flight.</summary>
    /// <remarks>
    /// Filtered to connector runs. Database maintenance and any other background job stay out of the
    /// connector screen, where they would only be confusing.
    /// </remarks>
    IReadOnlyList<JobProgressSnapshot> ActiveRuns { get; }

    /// <summary>Checks a run request before it is started or saved to a schedule.</summary>
    /// <param name="payload">What the run would do.</param>
    /// <returns><see langword="null"/> when the request is usable, otherwise the reason it is not.</returns>
    string? Validate(ConnectorJobPayload payload);

    /// <summary>Starts a connector run in the background and returns as soon as it has a run row.</summary>
    /// <param name="payload">What to read.</param>
    /// <returns>The run key, for <see cref="Cancel"/> and <see cref="GetReportAsync"/>.</returns>
    /// <exception cref="ConnectorException">The request is not usable; the message says why.</exception>
    /// <remarks>
    /// Returns while the run is still going. The caller watches <see cref="ActiveRuns"/> for the run
    /// with this key and reads <see cref="GetReportAsync"/> once it disappears from that list.
    /// </remarks>
    Task<long> StartAsync(ConnectorJobPayload payload);

    /// <summary>Asks a running connector run to stop.</summary>
    /// <param name="runId">The run key.</param>
    /// <returns><see langword="true"/> when a matching run was found and asked to stop.</returns>
    /// <remarks>
    /// Cooperative and not instant: the run stops at its next item boundary. Everything already
    /// ingested stays in the library, and the finished report says so.
    /// </remarks>
    bool Cancel(long runId);

    /// <summary>Reads back one run in full, with every item and every reason.</summary>
    /// <param name="runId">The run key.</param>
    /// <returns>The report, or <see langword="null"/> when no such run is in recent history.</returns>
    Task<ConnectorRunReport?> GetReportAsync(long runId);

    /// <summary>Lists recent connector runs, newest first.</summary>
    /// <param name="limit">Maximum reports to return.</param>
    /// <returns>Recent connector runs, each with its items.</returns>
    Task<IReadOnlyList<ConnectorRunReport>> ListRecentAsync(int limit);
}
