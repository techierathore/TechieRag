namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// One of the current user's licenses as returned by <c>GET /LicenseSvc</c>. Richer than
/// <see cref="ActiveLicenseData"/>: carries the license key and device/quantity detail the
/// billing screen shows (REQ-UI-030 / BRD-77).
/// </summary>
public sealed class UserLicenseData
{
    /// <summary>Gets or sets the license identifier.</summary>
    public int LicenseId { get; set; }

    /// <summary>Gets or sets the license key (e.g. <c>LIC-ABC123-XYZ789</c>).</summary>
    public string? LicenseKey { get; set; }

    /// <summary>Gets or sets the license display name (e.g. <c>Professional</c>).</summary>
    public string? LicenseName { get; set; }

    /// <summary>Gets or sets the licensing model (e.g. <c>Subscription</c>).</summary>
    public string? LicenseModel { get; set; }

    /// <summary>Gets or sets the license status (e.g. <c>Active</c>).</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the application the license is scoped to.</summary>
    public int? ApplicationId { get; set; }

    /// <summary>Gets or sets the application display name.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>Gets or sets the purchase timestamp.</summary>
    public DateTimeOffset? PurchaseDate { get; set; }

    /// <summary>Gets or sets the activation timestamp.</summary>
    public DateTimeOffset? ActivationDate { get; set; }

    /// <summary>Gets or sets the expiry timestamp.</summary>
    public DateTimeOffset? ExpiryDate { get; set; }

    /// <summary>Gets or sets the number of days remaining before expiry.</summary>
    public int? DaysRemaining { get; set; }

    /// <summary>Gets or sets the remaining quantity for quantity-model licenses.</summary>
    public int? RemainingQuantity { get; set; }

    /// <summary>Gets or sets the maximum number of devices this license may activate.</summary>
    public int? MaxDevices { get; set; }

    /// <summary>Gets or sets the number of devices currently activated against this license.</summary>
    public int? ActivatedDevices { get; set; }
}
