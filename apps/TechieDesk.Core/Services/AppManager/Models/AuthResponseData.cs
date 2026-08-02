namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Payload of <c>POST /AuthSvc/login</c> and <c>POST /AuthSvc/register</c> — user identity,
/// app-scoped role, JWT tokens, and the active license.
/// </summary>
public sealed class AuthResponseData
{
    /// <summary>Gets or sets the user identifier.</summary>
    public int UserId { get; set; }

    /// <summary>Gets or sets the user's email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's first name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's last name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Gets or sets the app-scoped application role code (Admin / Manager / User).</summary>
    public string? ApplicationRole { get; set; }

    /// <summary>Gets or sets the AppManager platform role (e.g. ApplicationUser).</summary>
    public string? AppManagerRole { get; set; }

    /// <summary>Gets or sets a value indicating whether the email address is verified.</summary>
    public bool IsEmailVerified { get; set; }

    /// <summary>Gets or sets the JWT access token.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the refresh token.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the access-token expiry timestamp.</summary>
    public DateTimeOffset TokenExpiresAt { get; set; }

    /// <summary>Gets or sets the user's active license for this application, if any.</summary>
    public ActiveLicenseData? ActiveLicense { get; set; }
}
