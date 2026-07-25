namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Registration details for <c>POST /AuthSvc/register</c>. The password is supplied separately
/// to <see cref="IAppManagerClient.RegisterAsync"/> so it can be RSA-encrypted just before
/// transmission and never stored on a model object.
/// </summary>
public sealed class RegisterRequest
{
    /// <summary>Gets or sets the new user's email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the new user's first name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Gets or sets the new user's last name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Gets or sets the new user's mobile number.</summary>
    public string? MobileNumber { get; set; }

    /// <summary>Gets or sets the application role to assign (defaults to the app's default role).</summary>
    public string? ApplicationRoleCode { get; set; }
}
