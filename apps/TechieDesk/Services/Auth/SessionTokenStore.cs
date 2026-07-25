namespace TechieDesk.Services.Auth;

/// <summary>
/// Per-circuit (scoped) server-side holder for the current session's user and JWT tokens
/// (BRD-15). Tokens live only in this server-side object — they are never written to browser
/// storage, cookies, or exposed to JavaScript.
/// </summary>
public sealed class SessionTokenStore
{
    private readonly object sync = new();

    /// <summary>Gets the current access token, or null when signed out.</summary>
    public string? AccessToken { get; private set; }

    /// <summary>Gets the current refresh token, or null when signed out.</summary>
    public string? RefreshToken { get; private set; }

    /// <summary>Gets the access-token expiry timestamp, or null when signed out.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Gets the signed-in user, or null when signed out.</summary>
    public TechieDeskUser? User { get; private set; }

    /// <summary>Gets a value indicating whether a session (token pair) is present.</summary>
    public bool HasSession => AccessToken != null;

    /// <summary>
    /// Establishes a session after login/registration.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="accessToken">The JWT access token.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="expiresAt">The access-token expiry timestamp.</param>
    public void SetSession(TechieDeskUser user, string accessToken, string refreshToken, DateTimeOffset expiresAt)
    {
        lock (sync)
        {
            User = user;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            ExpiresAt = expiresAt;
        }
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
        }
    }
}
