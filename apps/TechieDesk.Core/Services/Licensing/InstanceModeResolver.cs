namespace TechieDesk.Services.Licensing;

/// <summary>
/// Pure mapping from a <see cref="LicenseStatus"/> to an <see cref="InstanceModeStatus"/>
/// (REQ-FN-044/045, BRD-142/143). Deliberately side-effect free and time-free so every branch is
/// exhaustively unit-testable, including the ones that only occur when AppManager is down.
/// <para>
/// <b>The whole of the degradation policy lives here</b>, in one place, so it can be read in one
/// sitting: every path that is not "an assigned, active, team-tier seat" returns
/// <see cref="InstanceMode.Individual"/>. There is no branch that returns a locked or read-only
/// result, because no such result exists (BRD-129).
/// </para>
/// <para>
/// <b>REQ-UI-055 / BRD-91.</b> Every branch below returns a resource KEY and its arguments, never
/// a sentence. The mode's own name travels as a <see cref="LocalizedArgument"/> because it is a
/// word this app owns; the tier name travels as a plain argument because it is the licence
/// server's and matching it is what decides entitlements.
/// </para>
/// </summary>
public static class InstanceModeResolver
{
    /// <summary>
    /// Resolves the instance mode and seat state from the current licence status.
    /// </summary>
    /// <param name="license">The licence status maintained by <see cref="ILicenseService"/>.</param>
    /// <param name="options">Licensing options carrying the tier-name maps.</param>
    /// <returns>The resolved mode; never lower than <see cref="InstanceMode.Individual"/>.</returns>
    public static InstanceModeStatus Resolve(LicenseStatus license, LicensingOptions options)
    {
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(options);

        var tier = license.LicenseName;
        var entitled = MapTier(tier, options);
        var fromCache = license.Availability == LicenseAvailability.Cached;

        return license.Availability switch
        {
            // No licence server configured at all: the BRD-129 account-free default.
            LicenseAvailability.Offline => Floor(
                tier,
                SeatState.None,
                license.ValidatedAt,
                fromCache: false,
                LicenseMessageKeys.ModeOffline,
                []),

            // Not validated yet in this circuit. Individual until proven otherwise — we open
            // usable and add entitlements, we never open locked and subtract them.
            LicenseAvailability.Unknown => Floor(
                tier,
                SeatState.Unverified,
                license.ValidatedAt,
                fromCache: false,
                LicenseMessageKeys.ModeNotChecked,
                []),

            LicenseAvailability.Live or LicenseAvailability.Cached =>
                ResolveFromLicence(license, entitled, tier, fromCache),

            // AppManager unreachable past the BRD-51 grace window. Paid features lock (that is
            // FeatureGate's job); the install does NOT. Mode falls back to the Individual floor.
            LicenseAvailability.GraceExpired => Floor(
                tier,
                SeatState.Unverified,
                license.ValidatedAt,
                fromCache: true,
                LicenseMessageKeys.ModeServerUnreachable,
                []),

            // AppManager reachable and answering "no valid licence".
            LicenseAvailability.Invalid => Floor(
                tier,
                SeatState.Unassigned,
                license.ValidatedAt,
                fromCache: false,
                LicenseMessageKeys.ModeNoSeat,
                []),

            _ => InstanceModeStatus.Individual
        };
    }

    /// <summary>Gets the resource key for an <see cref="InstanceMode"/>'s own name.</summary>
    /// <param name="mode">The mode to label.</param>
    /// <returns>The key the licence card's badge already uses.</returns>
    /// <remarks>REQ-UI-055: exposed so the badge and the sentence beside it cannot disagree.</remarks>
    public static string ModeLabelKey(InstanceMode mode) => LicenseMessageKeys.ForMode(mode);

    /// <summary>
    /// Handles the two states that carry a real licence payload — freshly validated
    /// (<see cref="LicenseAvailability.Live"/>) and served from the persisted cache within the
    /// grace window (<see cref="LicenseAvailability.Cached"/>). Both are treated identically on
    /// purpose: BRD-51 says a cached licence is honoured, so a Team seat stays a Team seat
    /// through an outage.
    /// </summary>
    private static InstanceModeStatus ResolveFromLicence(
        LicenseStatus license, InstanceMode entitled, string? tier, bool fromCache)
    {
        var seatActive = string.Equals(license.Status, "Active", StringComparison.OrdinalIgnoreCase);

        // A personal/individual tier — no organisation seat is involved at all. The plan name is
        // quoted only when there is one, which is two resource keys rather than one key plus a
        // glued-on suffix: a sentence assembled from fragments cannot be translated well.
        if (entitled == InstanceMode.Individual)
        {
            var named = !string.IsNullOrWhiteSpace(tier);
            return Floor(
                tier,
                SeatState.None,
                license.ValidatedAt,
                fromCache,
                (seatActive, named) switch
                {
                    (true, true) => LicenseMessageKeys.ModePersonalOnPlan,
                    (true, false) => LicenseMessageKeys.ModePersonal,
                    (false, true) => LicenseMessageKeys.ModePersonalInactiveOnPlan,
                    (false, false) => LicenseMessageKeys.ModePersonalInactive
                },
                named ? [tier] : []);
        }

        // A team-tier licence with an active seat: this is the only path that grants Team or
        // Enterprise entitlements.
        if (seatActive)
        {
            return new InstanceModeStatus
            {
                Mode = entitled,
                Seat = SeatState.Assigned,
                TierName = tier,
                IsFromCache = fromCache,
                ResolvedAt = license.ValidatedAt,
                MessageKey = fromCache
                    ? LicenseMessageKeys.ModeSeatAssignedCached
                    : LicenseMessageKeys.ModeSeatAssigned,
                MessageArguments = fromCache
                    ? [new LocalizedArgument(ModeLabelKey(entitled))]
                    : [new LocalizedArgument(ModeLabelKey(entitled)), tier]
            };
        }

        // A team-tier licence whose seat is no longer honouring entitlements. This is the
        // REQ-FN-045 acceptance-(3) path and the BRD-129 clause: degrade, never lock.
        var seat = ClassifyLapsedSeat(license.Status);
        return Floor(
            tier,
            seat,
            license.ValidatedAt,
            fromCache,
            LapsedSeatMessageKey(seat),
            [tier]);
    }

    /// <summary>
    /// Maps a lapsed seat to the WHOLE sentence that explains it, rather than to an adjective
    /// dropped into a shared frame.
    /// </summary>
    /// <param name="seat">The classified seat state.</param>
    /// <returns>A resource key taking the mode label as <c>{0}</c> and the tier as <c>{1}</c>.</returns>
    /// <remarks>
    /// REQ-UI-055: "your {tier} seat is {expired}" reads as one clause in English and as several
    /// different constructions elsewhere. Four complete sentences translate; one sentence with an
    /// adjective slot does not, and it is the shape that produces the worst machine translations.
    /// </remarks>
    private static string LapsedSeatMessageKey(SeatState seat) => seat switch
    {
        SeatState.Expired => LicenseMessageKeys.ModeSeatExpired,
        SeatState.Revoked => LicenseMessageKeys.ModeSeatRevoked,
        SeatState.Unverified => LicenseMessageKeys.ModeSeatUnverified,
        _ => LicenseMessageKeys.ModeSeatUnassigned
    };

    /// <summary>
    /// Maps an AppManager status string on a team-tier licence to the reason the seat is not
    /// entitling. Anything unrecognised is treated as unassigned rather than as an error — an
    /// unknown status must not be able to invent a harsher outcome than the ones enumerated.
    /// </summary>
    private static SeatState ClassifyLapsedSeat(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return SeatState.Unassigned;
        }

        if (status.Contains("Expired", StringComparison.OrdinalIgnoreCase))
        {
            return SeatState.Expired;
        }

        if (status.Contains("Revoked", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Canceled", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Suspended", StringComparison.OrdinalIgnoreCase))
        {
            return SeatState.Revoked;
        }

        return SeatState.Unassigned;
    }

    /// <summary>
    /// Maps an AppManager licence tier name to the mode it entitles. Unknown and absent tiers map
    /// to <see cref="InstanceMode.Individual"/>: the tier list is additive, so a tier the app has
    /// never heard of grants nothing extra rather than taking anything away.
    /// </summary>
    private static InstanceMode MapTier(string? tier, LicensingOptions options)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return InstanceMode.Individual;
        }

        if (options.EnterpriseLicenseTiers.Contains(tier))
        {
            return InstanceMode.Enterprise;
        }

        return options.TeamLicenseTiers.Contains(tier)
            ? InstanceMode.Team
            : InstanceMode.Individual;
    }

    /// <summary>
    /// Builds an Individual-floor result. Every non-entitling branch funnels through here, which
    /// is what makes "degradation can only ever reach Individual" true by construction rather
    /// than by review.
    /// </summary>
    /// <remarks>
    /// Every Floor message opens with the mode's own name, so the mode label is prepended here
    /// once as argument <c>{0}</c> rather than at each of the seven call sites (REQ-UI-055).
    /// </remarks>
    private static InstanceModeStatus Floor(
        string? tier,
        SeatState seat,
        DateTime? resolvedAt,
        bool fromCache,
        string messageKey,
        IReadOnlyList<object?> messageArguments)
        => new()
        {
            Mode = InstanceMode.Individual,
            Seat = seat,
            TierName = tier,
            IsFromCache = fromCache,
            ResolvedAt = resolvedAt,
            MessageKey = messageKey,
            MessageArguments =
            [
                new LocalizedArgument(ModeLabelKey(InstanceMode.Individual)),
                .. messageArguments
            ]
        };
}
