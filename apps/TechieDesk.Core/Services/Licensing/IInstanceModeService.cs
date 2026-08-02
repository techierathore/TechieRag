namespace TechieDesk.Services.Licensing;

/// <summary>
/// Resolves and exposes this install's <see cref="InstanceMode"/> — Individual, Team or
/// Enterprise — from the AppManager licence tier (REQ-FN-044 / BRD-142), together with the state
/// of the organisation seat backing it (REQ-FN-045 / BRD-143). Scoped per circuit, like
/// <see cref="ILicenseService"/>, and memoized within the circuit.
/// <para>
/// It holds <b>no cache of its own</b>: it resolves from the <see cref="LicenseStatus"/> that
/// <see cref="ILicenseService"/> already persists through <c>ILicenseCacheRepository</c>, so the
/// BRD-51 grace window and the survive-a-restart guarantee are inherited rather than duplicated.
/// </para>
/// <para>
/// <b>What this is not:</b> it is not an authorization service. It has no notion of a role, a
/// capability or a workspace assignment (ADR-012 — those were deleted by REQ-FN-041 and are not
/// coming back), it never partitions data, and <b>nothing it returns can make the user's own
/// local data unreachable</b> (BRD-129). Consumers use it to decide whether to <i>show</i> team
/// features; <see cref="IFeatureGate"/> remains the authority on paid feature codes.
/// </para>
/// </summary>
public interface IInstanceModeService
{
    /// <summary>
    /// Gets the most recently resolved mode for this circuit. Defaults to
    /// <see cref="InstanceModeStatus.Individual"/> — fully usable — before the first resolution.
    /// </summary>
    InstanceModeStatus Current { get; }

    /// <summary>
    /// Resolves the mode, re-validating the licence only when <see cref="ILicenseService"/>
    /// considers its status stale (the REQ-FN-013 re-validation interval).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current mode; on any failure, the fully usable Individual floor.</returns>
    Task<InstanceModeStatus> EnsureFreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a fresh licence validation and re-resolves the mode. This is the "next successful
    /// check" of REQ-FN-045 acceptance (3): a seat revoked in AppManager degrades the install to
    /// Individual here — it never locks it.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The re-resolved mode; on any failure, the fully usable Individual floor.</returns>
    Task<InstanceModeStatus> RefreshAsync(CancellationToken cancellationToken = default);
}
