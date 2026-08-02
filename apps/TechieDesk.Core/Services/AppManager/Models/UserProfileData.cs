namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Payload of <c>GET /UserSvc/profile</c>. When called with the app's API key headers the
/// <see cref="ApplicationRole"/> is scoped to this application only.
/// </summary>
public sealed class UserProfileData
{
    /// <summary>Gets or sets the user identifier.</summary>
    public int UserId { get; set; }

    /// <summary>Gets or sets the user's email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's first name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's last name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's mobile number.</summary>
    public string? MobileNumber { get; set; }

    /// <summary>Gets or sets the user's avatar URL.</summary>
    public string? ProfileImageUrl { get; set; }

    /// <summary>Gets or sets the app-scoped application role code.</summary>
    public string? ApplicationRole { get; set; }

    /// <summary>Gets or sets a value indicating whether the email address is verified.</summary>
    public bool IsEmailVerified { get; set; }

    /// <summary>Gets or sets a value indicating whether the mobile number is verified.</summary>
    public bool IsMobileVerified { get; set; }

    /// <summary>Gets or sets the account creation timestamp.</summary>
    public DateTimeOffset? CreatedDate { get; set; }
}
