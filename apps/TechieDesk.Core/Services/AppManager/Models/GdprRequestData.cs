namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Payload of <c>POST /UserSvc/data-export</c> and <c>POST /UserSvc/delete-request</c> —
/// the accepted GDPR request's identifier and estimated completion date.
/// </summary>
public sealed class GdprRequestData
{
    /// <summary>Gets or sets the GDPR request identifier.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Gets or sets the confirmation message.</summary>
    public string? Message { get; set; }

    /// <summary>Gets or sets the estimated completion timestamp.</summary>
    public DateTimeOffset? EstimatedCompletionDate { get; set; }
}
