namespace TechieDesk.Services.Auth;

/// <summary>
/// Silent access-token refresh (BRD-15): called on demand ahead of expiry so the access token
/// is always fresh before an AppManager call is made on the user's behalf.
/// </summary>
public interface ITokenRefresher
{
    /// <summary>
    /// Ensures the session's access token is valid, refreshing it via
    /// <c>POST /AuthSvc/refresh</c> when it expires within the configured lead window.
    /// On refresh failure the session is cleared so route protection redirects to login.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// True when a valid token is available (always true in offline mode); false when there is
    /// no session or the refresh failed and the session was signed out.
    /// </returns>
    Task<bool> EnsureValidTokenAsync(CancellationToken cancellationToken = default);
}
