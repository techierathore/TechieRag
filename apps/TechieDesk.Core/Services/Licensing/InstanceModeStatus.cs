using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Licensing;

/// <summary>
/// Immutable snapshot of the resolved <see cref="InstanceMode"/> and seat state
/// (REQ-FN-044/045, BRD-142/143). Produced by <see cref="InstanceModeResolver"/> from the
/// <see cref="LicenseStatus"/> that <see cref="ILicenseService"/> already maintains, so it
/// inherits the persisted <c>LicenseCache</c> and the BRD-51 grace window for free rather than
/// keeping a second cache of its own.
/// <para>
/// <b>Invariant (BRD-129, absolute):</b> this type cannot express "local data is unavailable".
/// There is no locked, read-only or expired member; <see cref="LocalDataAccessible"/> is a
/// constant. Every degradation lands on <see cref="InstanceMode.Individual"/> with full local
/// capability. Licensing gates paid FEATURES, never access to the user's own documents.
/// </para>
/// </summary>
public sealed record InstanceModeStatus
{
    /// <summary>Gets the resolved licensing mode. Never lower than <see cref="InstanceMode.Individual"/>.</summary>
    public InstanceMode Mode { get; init; } = InstanceMode.Individual;

    /// <summary>Gets the state of this install's organisation seat, when one is involved.</summary>
    public SeatState Seat { get; init; } = SeatState.None;

    /// <summary>
    /// Gets the AppManager licence tier name the mode was resolved from (e.g. <c>Team</c>,
    /// <c>Enterprise</c>, <c>Professional</c>), when known.
    /// </summary>
    /// <remarks>
    /// <b>Culture-invariant (REQ-UI-055).</b> It is copied straight from
    /// <see cref="LicenseStatus.LicenseName"/>, which is the value
    /// <see cref="InstanceModeResolver"/> matches against the configured tier maps. It is quoted
    /// INTO the localized sentences as a formatting argument and is never itself translated —
    /// <c>Pricing.Tier.Name</c> records the same rule for the same reason.
    /// </remarks>
    public string? TierName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the mode was resolved from the cached last-known-good
    /// licence during an AppManager outage, within the BRD-51 grace window.
    /// </summary>
    public bool IsFromCache { get; init; }

    /// <summary>Gets the UTC timestamp of the licence validation this mode was resolved from, when known.</summary>
    public DateTime? ResolvedAt { get; init; }

    /// <summary>
    /// Gets the resource key for the sentence describing the current mode and seat state
    /// (REQ-UI-055 / BRD-91).
    /// </summary>
    /// <remarks>
    /// A key from <see cref="LicenseMessageKeys"/>, never a sentence. Every one of these messages
    /// opens with the mode's own name, which is why <see cref="MessageArguments"/> may carry a
    /// <see cref="LocalizedArgument"/>: "Team" is a word this app owns and translates, whereas the
    /// tier name beside it belongs to the licence server and does not.
    /// </remarks>
    public string MessageKey { get; init; } = LicenseMessageKeys.ModeFloor;

    /// <summary>Gets the format arguments <see cref="MessageKey"/> takes, if any.</summary>
    public IReadOnlyList<object?> MessageArguments { get; init; } =
        [new LocalizedArgument(LicenseMessageKeys.ForMode(InstanceMode.Individual))];

    /// <summary>Renders <see cref="MessageKey"/> in the reader's language.</summary>
    /// <param name="localize">The renderer's localizer, e.g. <c>(k, a) =&gt; Localizer[k, a!].Value</c>.</param>
    /// <returns>The translated sentence.</returns>
    public string Describe(LocalizeText localize)
        => LicenseMessage.Resolve(localize, MessageKey, MessageArguments);

    /// <summary>Gets a value indicating whether an organisation seat currently entitles this install.</summary>
    public bool IsTeamOrEnterprise => Mode is InstanceMode.Team or InstanceMode.Enterprise;

    /// <summary>
    /// Gets a value indicating whether team-specific surfaces should be visible. This is a
    /// <b>visibility</b> signal only — <see cref="IFeatureGate"/> remains the sole authority on
    /// whether a paid feature code is permitted, and neither gates local data.
    /// </summary>
    public bool TeamFeaturesVisible => IsTeamOrEnterprise;

    /// <summary>
    /// Gets a value indicating whether a team-tier seat exists but is not currently honouring
    /// Team/Enterprise entitlements (unassigned, expired, revoked or unverifiable). Drives an
    /// informational line on <c>/profile</c> and <c>/billing</c> — never a block or a nag.
    /// </summary>
    public bool IsSeatDegraded => Seat is SeatState.Unassigned
        or SeatState.Expired
        or SeatState.Revoked
        or SeatState.Unverified;

    /// <summary>
    /// Gets a value indicating whether the user's own local data is reachable. <b>Always true, by
    /// construction (BRD-129).</b> No mode, seat state, licence tier or AppManager outcome can
    /// make this false — the property exists so the invariant is asserted in the type system and
    /// in <c>InstanceModeTests</c> rather than merely documented.
    /// </summary>
    public bool LocalDataAccessible => true;

    /// <summary>
    /// The Individual floor state used before any resolution and whenever resolution itself
    /// fails. Fully usable: this is the BRD-129 account-free default.
    /// </summary>
    public static InstanceModeStatus Individual { get; } = new()
    {
        Mode = InstanceMode.Individual,
        Seat = SeatState.None,
        MessageKey = LicenseMessageKeys.ModeFloor,
        MessageArguments = [new LocalizedArgument(LicenseMessageKeys.ForMode(InstanceMode.Individual))]
    };
}
