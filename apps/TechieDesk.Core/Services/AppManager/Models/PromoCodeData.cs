namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// A validated promotional code from <c>POST /PaymentSvc/promo-codes/validate</c>
/// (REQ-FN-026 / BRD-79). Only returned when the code passed server-side validation; every
/// rejection arrives as an <see cref="AppManagerException"/> carrying a <c>PROMO_CODE_*</c> code.
/// </summary>
public sealed class PromoCodeData
{
    /// <summary>Gets or sets the promo code as the server echoes it back.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the discount type (<c>Percentage</c> or <c>FixedAmount</c>).</summary>
    public string? DiscountType { get; set; }

    /// <summary>Gets or sets the discount magnitude, interpreted per <see cref="DiscountType"/>.</summary>
    public decimal DiscountValue { get; set; }

    /// <summary>Gets or sets the human-readable description of what the code does.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets when the code stops being valid.</summary>
    public DateTimeOffset? ExpiryDate { get; set; }
}
