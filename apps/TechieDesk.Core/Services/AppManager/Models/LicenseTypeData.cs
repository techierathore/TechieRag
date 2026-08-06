namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// A purchasable license type as returned by <c>GET /LicenseSvc/types</c> — the catalogue that
/// backs the pricing screen (REQ-UI-029 / BRD-76).
/// </summary>
public sealed class LicenseTypeData
{
    /// <summary>Gets or sets the license type identifier.</summary>
    public int LicenseTypeId { get; set; }

    /// <summary>Gets or sets the display name (e.g. <c>Professional</c>).</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable type code (e.g. <c>PRO</c>).</summary>
    public string? TypeCode { get; set; }

    /// <summary>Gets or sets the marketing description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the licensing model (e.g. <c>Subscription</c>, <c>Perpetual</c>).</summary>
    public string? LicenseModel { get; set; }

    /// <summary>Gets or sets the maximum number of activated devices, when capped.</summary>
    public int? MaxDevices { get; set; }

    /// <summary>Gets or sets the license duration in days, when time-limited.</summary>
    public int? DurationDays { get; set; }

    /// <summary>Gets or sets the included quantity for quantity-model licenses.</summary>
    public int? Quantity { get; set; }

    /// <summary>Gets or sets the per-currency price list.</summary>
    public List<LicensePricingData> Pricing { get; set; } = new();
}

/// <summary>
/// One currency's price for a <see cref="LicenseTypeData"/>.
/// </summary>
public sealed class LicensePricingData
{
    /// <summary>Gets or sets the ISO currency code (e.g. <c>USD</c>).</summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the numeric amount in that currency.</summary>
    public decimal Amount { get; set; }

    /// <summary>Gets or sets the server-formatted price string (e.g. <c>$99.99</c>).</summary>
    public string? FormattedPrice { get; set; }
}
