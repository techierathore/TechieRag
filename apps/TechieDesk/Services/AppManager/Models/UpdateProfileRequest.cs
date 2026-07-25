namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Body of <c>PUT /UserSvc/profile</c> — updatable profile fields.
/// </summary>
public sealed class UpdateProfileRequest
{
    /// <summary>Gets or sets the user's first name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's last name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's mobile number.</summary>
    public string? MobileNumber { get; set; }

    /// <summary>Gets or sets the user's avatar URL.</summary>
    public string? ProfileImageUrl { get; set; }
}
