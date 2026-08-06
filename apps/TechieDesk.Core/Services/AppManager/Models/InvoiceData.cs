namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// A single invoice from <c>GET /PaymentSvc/invoices</c>. The PDF itself is fetched separately
/// through <c>GET /PaymentSvc/invoices/{aInvoiceId}/download</c> (REQ-UI-031 / BRD-78).
/// </summary>
public sealed class InvoiceData
{
    /// <summary>Gets or sets the invoice identifier.</summary>
    public int InvoiceId { get; set; }

    /// <summary>Gets or sets the human-readable invoice number (e.g. <c>INV-2026-0142</c>).</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>Gets or sets the invoice date.</summary>
    public DateTimeOffset InvoiceDate { get; set; }

    /// <summary>Gets or sets the payment due date.</summary>
    public DateTimeOffset? DueDate { get; set; }

    /// <summary>Gets or sets the pre-tax subtotal.</summary>
    public decimal SubTotal { get; set; }

    /// <summary>Gets or sets the tax component.</summary>
    public decimal TaxAmount { get; set; }

    /// <summary>Gets or sets the total payable amount.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Gets or sets the ISO currency code.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Gets or sets the invoice status (e.g. <c>Paid</c>, <c>Due</c>).</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the date the invoice was settled, when it has been.</summary>
    public DateTimeOffset? PaidDate { get; set; }
}
