using TechieDesk.Services.AppManager.Models;

namespace TechieDesk.Services.Auth;

/// <summary>
/// Server-side holder for one session's user, JWT tokens, and active license (BRD-15). Tokens
/// live only in this server-side object — they are never written to browser storage, cookies, or
/// exposed to JavaScript (REQ-NFR-004).
/// </summary>
/// <remarks>
/// REQ-FN-032: instances are owned by <see cref="ISessionStore"/> and keyed by an opaque handle,
/// NOT by the Blazor circuit. Every circuit and HTTP request presenting the same handle resolves
/// the same instance, so destroying a circuit (a full-page navigation, an F5 refresh) no longer
/// destroys the session.
/// </remarks>
public sealed class SessionTokenStore
{
    private readonly object sync = new();

    /// <summary>
    /// Raised after the stored session state changes, so an owner can mirror it somewhere durable.
    /// </summary>
    /// <remarks>
    /// REQ-FN-039: <see cref="SessionStore"/> subscribes to this so that a silent token refresh —
    /// which replaces the refresh token in place, without going anywhere near the store — is written
    /// back to the OS credential store. Without it the persisted copy would go stale at the first
    /// refresh and "a restart restores the session" would hold only until the token rotated once.
    /// <para>
    /// Raised outside <c>sync</c>, so a handler is free to read this instance without deadlocking.
    /// </para>
    /// </remarks>
    public event Action<SessionTokenStore>? Changed;

    /// <summary>Gets the current access token, or null when signed out.</summary>
    public string? AccessToken { get; private set; }

    /// <summary>Gets the current refresh token, or null when signed out.</summary>
    public string? RefreshToken { get; private set; }

    /// <summary>Gets the access-token expiry timestamp, or null when signed out.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Gets the signed-in user, or null when signed out.</summary>
    public TechieDeskUser? User { get; private set; }

    /// <summary>
    /// Gets the active license captured from the login response (REQ-UI-007 / BRD-13), or null
    /// when the account holds none.
    /// </summary>
    public ActiveLicenseData? ActiveLicense { get; private set; }

    /// <summary>Gets a value indicating whether a session (token pair) is present.</summary>
    public bool HasSession => AccessToken != null;

    /// <summary>
    /// Establishes a session after login/registration.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="accessToken">The JWT access token.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="expiresAt">The access-token expiry timestamp.</param>
    /// <param name="activeLicense">The active license from the login response (BRD-13), if any.</param>
    public void SetSession(
        TechieDeskUser user,
        string accessToken,
        string refreshToken,
        DateTimeOffset expiresAt,
        ActiveLicenseData? activeLicense = null)
    {
        lock (sync)
        {
            User = user;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            ExpiresAt = expiresAt;
            ActiveLicense = activeLicense;
        }

        Changed?.Invoke(this);
    }

    /// <summary>
    /// Replaces the token pair after a silent refresh, keeping the signed-in user.
    /// </summary>
    /// <param name="accessToken">The new JWT access token.</param>
    /// <param name="refreshToken">The new refresh token.</param>
    /// <param name="expiresAt">The new access-token expiry timestamp.</param>
    public void UpdateTokens(string accessToken, string refreshToken, DateTimeOffset expiresAt)
    {
        lock (sync)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            ExpiresAt = expiresAt;
        }

        Changed?.Invoke(this);
    }

    /// <summary>Clears the session (logout or failed refresh).</summary>
    public void Clear()
    {
        lock (sync)
        {
            User = null;
            AccessToken = null;
            RefreshToken = null;
            ExpiresAt = null;
            ActiveLicense = null;
        }

        Changed?.Invoke(this);
    }
}
