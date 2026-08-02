namespace TechieDesk.Services.Data;

/// <summary>
/// Cached AppManager license payload per user, enabling the outage grace window
/// (BRD-101 resilience; BRD-104 P1 schema). Unique per UserId.
/// </summary>
public sealed class LicenseCache
{
    /// <summary>Primary key.</summary>
    public long LicenseCacheId { get; set; }

    /// <summary>AppManager user identifier.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>License payload as JSON, exactly as validated.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the last successful validation.</summary>
    public DateTime ValidatedAt { get; set; }
}
