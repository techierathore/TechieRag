using TechieDesk.Services.Localization;

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
/// <remarks>
/// <para>
/// <b>REQ-UI-055 / BRD-91.</b> This record used to carry <c>Message</c>, an English sentence built
/// in <see cref="LicenseService"/>. It is rendered in the ALWAYS-VISIBLE shell banner
/// (<c>MainLayout</c>), so on a Hindi install it was English on every screen of the product — the
/// single highest-visibility unlocalized string that survived REQ-UI-051. It is now
/// <see cref="MessageKey"/> plus <see cref="MessageArguments"/>, resolved by whatever renders it
/// (<see cref="Describe"/>). The record has nothing English left to render.
/// </para>
/// <para>
/// <b><see cref="LicenseName"/> and <see cref="Status"/> are deliberately NOT localized.</b> They
/// are wire values that entitlement decisions are matched against — see the remarks on each — and
/// translating either is a billing bug, not a cosmetic one.
/// </para>
/// </remarks>
public sealed record LicenseStatus
{
    /// <summary>Gets how the status was determined.</summary>
    public LicenseAvailability Availability { get; init; } = LicenseAvailability.Unknown;

    /// <summary>Gets the license display name (e.g. Professional), when known.</summary>
    /// <remarks>
    /// <b>Culture-invariant, and load-bearing.</b> This is AppManager's <c>licenseName</c> and it is
    /// MATCHED, not merely shown: <see cref="InstanceModeResolver"/> looks it up in
    /// <see cref="LicensingOptions.TeamLicenseTiers"/> and
    /// <see cref="LicensingOptions.EnterpriseLicenseTiers"/> to decide entitlements, and
    /// <c>Pricing.IsCurrent</c> prefix-matches it against the published tier names to highlight the
    /// plan you hold. The same rule <c>Pricing.Tier.Name</c> already records — invariant name,
    /// separate display text — applies here, which is why the licence card renders this value as it
    /// arrives rather than translating it. A translated tier name would stop matching the server and
    /// silently downgrade a paying team to Individual.
    /// </remarks>
    public string? LicenseName { get; init; }

    /// <summary>Gets the raw license status string (e.g. Active), when known.</summary>
    /// <remarks>
    /// <b>Culture-invariant.</b> Compared against <c>Active</c> by <see cref="IsActive"/> and by
    /// <see cref="InstanceModeResolver"/>, and scanned for <c>Expired</c>/<c>Revoked</c>/
    /// <c>Cancelled</c>/<c>Suspended</c> to classify a lapsed seat. It is server vocabulary.
    /// </remarks>
    public string? Status { get; init; }

    /// <summary>Gets the license expiry timestamp, when known.</summary>
    public DateTimeOffset? ExpiryDate { get; init; }

    /// <summary>Gets the number of days remaining before expiry, when known.</summary>
    public int? DaysRemaining { get; init; }

    /// <summary>Gets the UTC timestamp the underlying data was last successfully validated.</summary>
    public DateTime? ValidatedAt { get; init; }

    /// <summary>
    /// Gets the resource key for the sentence describing the current state (REQ-UI-055).
    /// </summary>
    /// <remarks>
    /// A key from <see cref="LicenseMessageKeys"/>, never a sentence. Resolve it with
    /// <see cref="Describe"/> rather than indexing a localizer directly, so that a key carrying a
    /// nested <see cref="LocalizedArgument"/> is translated too.
    /// </remarks>
    public string MessageKey { get; init; } = LicenseMessageKeys.StateNotValidated;

    /// <summary>Gets the format arguments <see cref="MessageKey"/> takes, if any.</summary>
    public IReadOnlyList<object?> MessageArguments { get; init; } = [];

    /// <summary>Renders <see cref="MessageKey"/> in the reader's language.</summary>
    /// <param name="localize">The renderer's localizer, e.g. <c>(k, a) =&gt; Localizer[k, a!].Value</c>.</param>
    /// <returns>The translated sentence.</returns>
    public string Describe(LocalizeText localize)
        => LicenseMessage.Resolve(localize, MessageKey, MessageArguments);

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
    /// <remarks>
    /// <c>LicenseName</c> stays <c>Free (offline)</c> in every culture. It is not a label:
    /// <c>Pricing.IsCurrent</c> asks whether it STARTS WITH a published tier name to decide which
    /// card to highlight, so an offline install highlights Free because of these exact characters.
    /// <c>Status</c> is <c>Active</c> for the same reason — <see cref="IsActive"/> compares it.
    /// </remarks>
    public static LicenseStatus Offline { get; } = new()
    {
        Availability = LicenseAvailability.Offline,
        LicenseName = "Free (offline)",
        Status = "Active",
        MessageKey = LicenseMessageKeys.StateOffline
    };

    /// <summary>The not-yet-validated state (AppManager mode, before the first validation).</summary>
    public static LicenseStatus Unknown { get; } = new()
    {
        Availability = LicenseAvailability.Unknown,
        MessageKey = LicenseMessageKeys.StateNotValidated
    };
}
