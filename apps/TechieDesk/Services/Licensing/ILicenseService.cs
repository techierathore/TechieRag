namespace TechieDesk.Services.Licensing;

/// <summary>
/// Validates the current user's license against AppManager <c>POST /LicenseSvc/validate</c>,
/// caches the last-known-good payload for the outage grace window, and exposes the current
/// status for the UI (REQ-FN-013/BRD-49, REQ-FN-015/BRD-51). Scoped per circuit.
/// </summary>
public interface ILicenseService
{
    /// <summary>Gets the most recently determined license status for this circuit.</summary>
    LicenseStatus Current { get; }

    /// <summary>
    /// Forces a fresh validation against AppManager. On success the payload is cached; when
    /// AppManager is unreachable the last-known-good cache is honored within the configured
    /// grace window, otherwise the state degrades to grace-expired.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resulting <see cref="LicenseStatus"/>.</returns>
    Task<LicenseStatus> ValidateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-validates only when the status is unknown or the configured re-validation interval has
    /// elapsed; otherwise returns the cached <see cref="Current"/> status. Called at login and on
    /// navigation to keep the status periodically fresh without a call per render.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current (possibly refreshed) <see cref="LicenseStatus"/>.</returns>
    Task<LicenseStatus> EnsureFreshAsync(CancellationToken cancellationToken = default);
}
