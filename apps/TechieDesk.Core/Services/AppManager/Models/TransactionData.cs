namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// A single payment-history row from <c>GET /PaymentSvc/transactions</c> (REQ-UI-031 / BRD-78).
/// </summary>
public sealed class TransactionData
{
    /// <summary>Gets or sets the transaction identifier.</summary>
    public int TransactionId { get; set; }

    /// <summary>Gets or sets the human-readable transaction number (e.g. <c>TXN-2026-0001</c>).</summary>
    public string? TransactionNumber { get; set; }

    /// <summary>Gets or sets the transaction type (e.g. <c>Purchase</c>, <c>Renewal</c>, <c>Refund</c>).</summary>
    public string? TransactionType { get; set; }

    /// <summary>Gets or sets the transaction amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Gets or sets the ISO currency code.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Gets or sets the status (e.g. <c>Completed</c>, <c>Processing</c>, <c>Failed</c>).</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the payment method description.</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>Gets or sets the transaction timestamp.</summary>
    public DateTimeOffset TransactionDate { get; set; }

    /// <summary>Gets or sets the free-text description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the upstream payment provider's transaction reference.</summary>
    public string? ProviderTransactionId { get; set; }
}
