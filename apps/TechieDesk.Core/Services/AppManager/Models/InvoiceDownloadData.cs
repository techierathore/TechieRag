namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// The bytes of an invoice PDF fetched from <c>GET /PaymentSvc/invoices/{aInvoiceId}/download</c>.
/// </summary>
/// <remarks>
/// TechieDesk does not generate invoice PDFs locally — AppManager is the system of record for
/// billing documents and renders them server-side, so this type carries the rendered bytes
/// through unchanged (REQ-UI-031 / BRD-78).
/// </remarks>
public sealed class InvoiceDownloadData
{
    /// <summary>Gets or sets the suggested file name, taken from Content-Disposition when present.</summary>
    public string FileName { get; set; } = "invoice.pdf";

    /// <summary>Gets or sets the MIME type the server reported (normally <c>application/pdf</c>).</summary>
    public string ContentType { get; set; } = "application/pdf";

    /// <summary>Gets or sets the raw document bytes.</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
