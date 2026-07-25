namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// License summary as returned inside login responses (<c>activeLicense</c>) and by
/// <c>POST /LicenseSvc/validate</c> (<c>license</c>).
/// </summary>
public sealed class ActiveLicenseData
{
    /// <summary>Gets or sets the license identifier.</summary>
    public int LicenseId { get; set; }

    /// <summary>Gets or sets the license display name (e.g. Professional).</summary>
    public string LicenseName { get; set; } = string.Empty;

    /// <summary>Gets or sets the license status (e.g. Active).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the application the license is scoped to.</summary>
    public int? ApplicationId { get; set; }

    /// <summary>Gets or sets the application display name.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>Gets or sets the license expiry timestamp.</summary>
    public DateTimeOffset? ExpiryDate { get; set; }

    /// <summary>Gets or sets the number of days remaining before expiry.</summary>
    public int? DaysRemaining { get; set; }
}
