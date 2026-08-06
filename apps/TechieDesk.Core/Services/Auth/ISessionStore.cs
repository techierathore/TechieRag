using TechieDesk.Services.AppManager.Models;

namespace TechieDesk.Services.Auth;

/// <summary>
/// Server-side store of authenticated sessions, keyed by the opaque handle carried in the
/// <see cref="SessionCookie.Name"/> cookie (REQ-FN-032).
/// </summary>
/// <remarks>
/// This is what makes a session survive the destruction of a Blazor circuit: the tokens live
/// here, shared across every circuit and HTTP request that presents the same handle, instead of
/// in a per-circuit object. Tokens still never leave the server (REQ-NFR-004) — only the handle
/// is given to the browser.
/// <para>
/// REQ-FN-039 re-backed the shipped implementation's <i>persistence</i> with the OS credential
/// store; it did NOT remove this seam. Three security properties live behind it and nowhere else —
/// the sliding + hard expiry bounds, the handle rotation on sign-in that defeats session fixation,
/// and "log out — all devices" (REQ-UI-008) — so collapsing straight to a
/// <see cref="SessionTokenStore"/> field would delete all three without a single failing test.
/// </para>
/// </remarks>
public interface ISessionStore
{
    /// <summary>Gets the number of live (unexpired) sessions.</summary>
    int ActiveSessionCount { get; }

    /// <summary>
    /// Creates a session and returns its freshly minted opaque handle. Callers must invalidate
    /// any pre-existing handle first so the handle rotates on every login.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="accessToken">The AppManager JWT access token.</param>
    /// <param name="refreshToken">The AppManager refresh token.</param>
    /// <param name="expiresAt">The access-token expiry timestamp.</param>
    /// <param name="activeLicense">The active license captured at login (BRD-13), if any.</param>
    /// <returns>The opaque handle to place in the session cookie.</returns>
    string CreateSession(
        TechieDeskUser user,
        string accessToken,
        string refreshToken,
        DateTimeOffset expiresAt,
        ActiveLicenseData? activeLicense);

    /// <summary>
    /// Resolves a handle to its shared session state, extending the sliding idle window.
    /// </summary>
    /// <param name="handle">The opaque handle from the session cookie, or null.</param>
    /// <returns>The shared session state, or null when the handle is unknown or expired.</returns>
    SessionTokenStore? Resolve(string? handle);

    /// <summary>
    /// Drops a single session (logout, or handle rotation on a new login).
    /// </summary>
    /// <param name="handle">The opaque handle, or null.</param>
    /// <returns>True when a session was removed.</returns>
    bool Invalidate(string? handle);

    /// <summary>
    /// Drops every session belonging to a user ("log out — all devices", REQ-UI-008).
    /// </summary>
    /// <param name="userId">The AppManager user identifier.</param>
    /// <returns>The number of sessions removed.</returns>
    int InvalidateAllForUser(int userId);

    /// <summary>
    /// Reinstates the session held in the OS credential store, so a restart does not ask the owner
    /// to sign in again (REQ-FN-039).
    /// </summary>
    /// <returns>
    /// A freshly minted handle for the restored session, or null when nothing was stored, the stored
    /// copy was unreadable, or it had already passed either of its expiry bounds.
    /// </returns>
    /// <remarks>
    /// Idempotent: calling it again while the restored session is still live returns the same handle
    /// rather than creating a second session. The restored session keeps the ORIGINAL hard deadline,
    /// so relaunching the app can never extend a session past its absolute lifetime.
    /// </remarks>
    string? RestorePersistedSession();
}
