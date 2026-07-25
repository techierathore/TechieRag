namespace TechieDesk.Services.Licensing;

/// <summary>
/// How the current license status was determined — drives the UI badge, the cached-license
/// banner (REQ-FN-015), and whether gated features are permitted.
/// </summary>
public enum LicenseAvailability
{
    /// <summary>Not yet validated in this circuit.</summary>
    Unknown = 0,

    /// <summary>Offline single-user mode — no AppManager configured; local Free tier (BRD-54).</summary>
    Offline,

    /// <summary>Freshly validated against AppManager <c>POST /LicenseSvc/validate</c>.</summary>
    Live,

    /// <summary>AppManager unreachable; serving the last-known-good license within the grace window.</summary>
    Cached,

    /// <summary>AppManager unreachable and the grace window has elapsed — degraded / features locked.</summary>
    GraceExpired,

    /// <summary>AppManager reachable but the license is invalid, expired, or absent.</summary>
    Invalid
}

/// <summary>
/// Immutable snapshot of the current user's license state (REQ-FN-013/BRD-49). Exposed by
/// <see cref="ILicenseService"/> and rendered by the license-status card and cached-license
/// banner.
/// </summary>
public sealed record LicenseStatus
{
    /// <summary>Gets how the status was determined.</summary>
    public LicenseAvailability Availability { get; init; } = LicenseAvailability.Unknown;

    /// <summary>Gets the license display name (e.g. Professional), when known.</summary>
    public string? LicenseName { get; init; }

    /// <summary>Gets the raw license status string (e.g. Active), when known.</summary>
    public string? Status { get; init; }

    /// <summary>Gets the license expiry timestamp, when known.</summary>
    public DateTimeOffset? ExpiryDate { get; init; }

    /// <summary>Gets the number of days remaining before expiry, when known.</summary>
    public int? DaysRemaining { get; init; }

    /// <summary>Gets the UTC timestamp the underlying data was last successfully validated.</summary>
    public DateTime? ValidatedAt { get; init; }

    /// <summary>Gets a human-readable message describing the current state.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether premium/gated features are permitted in this state:
    /// true when live, offline (Free tier still functions), or cached within grace; false once
    /// grace has expired or the license is invalid.
    /// </summary>
    public bool FeaturesPermitted =>
        Availability is LicenseAvailability.Live
            or LicenseAvailability.Offline
            or LicenseAvailability.Cached;

    /// <summary>Gets a value indicating whether the status is being served from the cache.</summary>
    public bool IsFromCache => Availability is LicenseAvailability.Cached or LicenseAvailability.GraceExpired;

    /// <summary>Gets a value indicating whether the license is considered active/valid.</summary>
    public bool IsActive =>
        Availability is LicenseAvailability.Live or LicenseAvailability.Cached
            && string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);

    /// <summary>The offline single-user state (Free tier, no AppManager).</summary>
    public static LicenseStatus Offline { get; } = new()
    {
        Availability = LicenseAvailability.Offline,
        LicenseName = "Free (offline)",
        Status = "Active",
        Message = "Offline single-user mode — running the local Free tier; no license server configured."
    };

    /// <summary>The not-yet-validated state (AppManager mode, before the first validation).</summary>
    public static LicenseStatus Unknown { get; } = new()
    {
        Availability = LicenseAvailability.Unknown,
        Message = "License not yet validated."
    };
}
