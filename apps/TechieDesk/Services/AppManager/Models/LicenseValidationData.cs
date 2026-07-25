namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Payload of <c>POST /LicenseSvc/validate</c> — whether the current user holds a valid
/// license for this application.
/// </summary>
public sealed class LicenseValidationData
{
    /// <summary>Gets or sets a value indicating whether the license is valid.</summary>
    public bool IsValid { get; set; }

    /// <summary>Gets or sets the validated license details, when valid.</summary>
    public ActiveLicenseData? License { get; set; }
}
