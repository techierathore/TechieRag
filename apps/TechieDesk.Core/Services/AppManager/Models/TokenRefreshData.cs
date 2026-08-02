namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Payload of <c>POST /AuthSvc/refresh</c> — a fresh access/refresh token pair.
/// </summary>
public sealed class TokenRefreshData
{
    /// <summary>Gets or sets the new JWT access token.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the new refresh token.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the new access-token expiry timestamp.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
