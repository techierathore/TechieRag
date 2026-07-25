namespace TechieDesk.Services.Data;

/// <summary>
/// GDPR data-export or account-deletion request submitted by a user (BRD-104 P1 schema).
/// </summary>
public sealed class GdprRequest
{
    /// <summary>Primary key.</summary>
    public long GdprRequestId { get; set; }

    /// <summary>AppManager user identifier of the requester.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Request type (e.g. Export, Delete).</summary>
    public string RequestType { get; set; } = string.Empty;

    /// <summary>Processing status (e.g. Pending, Completed, Rejected).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the request was submitted.</summary>
    public DateTime RequestedAt { get; set; }
}
