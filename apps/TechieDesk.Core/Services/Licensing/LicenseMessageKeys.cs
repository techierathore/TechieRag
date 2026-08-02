using TechieDesk.Services.AppManager;

namespace TechieDesk.Services.Licensing;

/// <summary>
/// Every resource key the licensing services can return, and the mapping from an AppManager
/// error CODE to the one that explains it (REQ-UI-055 / BRD-91).
/// </summary>
/// <remarks>
/// <para>
/// <b>Keyed off the code, never off the English.</b> AppManager answers in
/// <c>SCREAMING_SNAKE_CASE</c> wire codes — <c>LICENSE_EXPIRED</c>, <c>RATE_LIMITED</c>,
/// <c>NO_APP_ACCESS</c> — which <see cref="AppManagerErrorMapper"/> already turns into the typed
/// <see cref="AppManagerError"/>. This class maps that ENUM to a key. It never looks at
/// <c>Exception.Message</c>, because that text is written by a server this app does not own: it
/// arrives in English, it is not part of any documented contract, and it can be reworded on the
/// server at any time. A localization keyed off it would silently fall back to the generic message
/// the first time somebody fixed a typo in AppManager.
/// </para>
/// <para>
/// <b>Unknown codes are not a defect.</b> A code this build has never seen maps to
/// <see cref="LicenseErrorGeneric"/> / <see cref="FeatureDeniedUpgradeRequired"/>, which say
/// something true and unalarming rather than guessing. The wire code itself is still logged, so
/// the operator keeps the diagnosable part.
/// </para>
/// <para>
/// The keys are constants rather than inline strings so that
/// <c>LicensingLocalizationTests</c> can enumerate them and prove every one resolves in both
/// shipped languages — the guard REQ-UI-051 established for service-owned keys.
/// </para>
/// </remarks>
public static class LicenseMessageKeys
{
    /// <summary>Offline single-user mode — no licence server is configured (BRD-54/129).</summary>
    public const string StateOffline = "LicenseStateOffline";

    /// <summary>AppManager mode, before the first validation of this circuit.</summary>
    public const string StateNotValidated = "LicenseStateNotValidated";

    /// <summary>Freshly validated. Takes the server-supplied plan name as <c>{0}</c>.</summary>
    public const string StateValidated = "LicenseStateValidated";

    /// <summary>AppManager answered, and the answer was "no valid licence".</summary>
    public const string StateNoValidLicense = "LicenseStateNoValidLicense";

    /// <summary>AppManager unreachable, serving the cached licence inside the grace window.</summary>
    public const string StateCached = "LicenseStateCached";

    /// <summary>AppManager unreachable and nothing was ever cached.</summary>
    public const string StateNoCachedLicense = "LicenseStateNoCachedLicense";

    /// <summary>The cached licence outlived the grace window. Takes the window in hours as <c>{0}</c>.</summary>
    public const string StateGraceExpired = "LicenseStateGraceExpired";

    /// <summary>An AppManager rejection this build has no specific wording for.</summary>
    public const string LicenseErrorGeneric = "LicenseErrorGeneric";

    /// <summary><c>LICENSE_EXPIRED</c>.</summary>
    public const string LicenseErrorExpired = "LicenseErrorExpired";

    /// <summary><c>LICENSE_NOT_FOUND</c>.</summary>
    public const string LicenseErrorNotFound = "LicenseErrorNotFound";

    /// <summary><c>LICENSE_INACTIVE</c>.</summary>
    public const string LicenseErrorInactive = "LicenseErrorInactive";

    /// <summary><c>CROSS_APP_LICENSE</c> / <c>APP_ID_MISMATCH</c>.</summary>
    public const string LicenseErrorCrossApplication = "LicenseErrorCrossApplication";

    /// <summary><c>NO_APP_ACCESS</c>.</summary>
    public const string LicenseErrorNoAppAccess = "LicenseErrorNoAppAccess";

    /// <summary><c>RATE_LIMITED</c>.</summary>
    public const string LicenseErrorRateLimited = "LicenseErrorRateLimited";

    /// <summary><c>UNAUTHORIZED</c> / <c>INVALID_TOKEN</c> / <c>SESSION_EXPIRED</c>.</summary>
    public const string LicenseErrorSessionExpired = "LicenseErrorSessionExpired";

    /// <summary><c>ACCOUNT_DISABLED</c>.</summary>
    public const string LicenseErrorAccountDisabled = "LicenseErrorAccountDisabled";

    /// <summary>Individual, offline single-user mode. Takes the mode label as <c>{0}</c>.</summary>
    public const string ModeOffline = "InstanceModeMessageOffline";

    /// <summary>Individual, licence not yet checked. Takes the mode label as <c>{0}</c>.</summary>
    public const string ModeNotChecked = "InstanceModeMessageNotChecked";

    /// <summary>Individual, seat unverifiable because the server is unreachable. <c>{0}</c> mode label.</summary>
    public const string ModeServerUnreachable = "InstanceModeMessageServerUnreachable";

    /// <summary>Individual, AppManager says no seat is assigned. <c>{0}</c> mode label.</summary>
    public const string ModeNoSeat = "InstanceModeMessageNoSeat";

    /// <summary>A personal licence with no plan name to quote. <c>{0}</c> mode label.</summary>
    public const string ModePersonal = "InstanceModeMessagePersonal";

    /// <summary>A personal licence on a named plan. <c>{0}</c> mode label, <c>{1}</c> plan name.</summary>
    public const string ModePersonalOnPlan = "InstanceModeMessagePersonalOnPlan";

    /// <summary>A personal licence that is not currently active. <c>{0}</c> mode label.</summary>
    public const string ModePersonalInactive = "InstanceModeMessagePersonalInactive";

    /// <summary>An inactive personal licence on a named plan. <c>{0}</c> mode, <c>{1}</c> plan.</summary>
    public const string ModePersonalInactiveOnPlan = "InstanceModeMessagePersonalInactiveOnPlan";

    /// <summary>An assigned, active organisation seat. <c>{0}</c> mode label, <c>{1}</c> plan name.</summary>
    public const string ModeSeatAssigned = "InstanceModeMessageSeatAssigned";

    /// <summary>An assigned seat served from the cache during an outage. <c>{0}</c> mode label.</summary>
    public const string ModeSeatAssignedCached = "InstanceModeMessageSeatAssignedCached";

    /// <summary>A team-tier licence whose seat has expired. <c>{0}</c> mode, <c>{1}</c> plan.</summary>
    public const string ModeSeatExpired = "InstanceModeMessageSeatExpired";

    /// <summary>A team-tier licence whose seat was revoked or suspended. <c>{0}</c> mode, <c>{1}</c> plan.</summary>
    public const string ModeSeatRevoked = "InstanceModeMessageSeatRevoked";

    /// <summary>A team-tier licence whose seat could not be verified. <c>{0}</c> mode, <c>{1}</c> plan.</summary>
    public const string ModeSeatUnverified = "InstanceModeMessageSeatUnverified";

    /// <summary>A team-tier licence with no seat assigned to this account. <c>{0}</c> mode, <c>{1}</c> plan.</summary>
    public const string ModeSeatUnassigned = "InstanceModeMessageSeatUnassigned";

    /// <summary>The Individual floor before anything has been resolved. <c>{0}</c> mode label.</summary>
    public const string ModeFloor = "InstanceModeMessageFloor";

    /// <summary>Nothing was named to gate on — a caller bug, phrased for whoever sees it.</summary>
    public const string FeatureDeniedNoFeature = "FeatureDeniedNoFeature";

    /// <summary>Offline Free tier. <c>{0}</c> feature label, <c>{1}</c> upgrade tier.</summary>
    public const string FeatureDeniedOfflineTier = "FeatureDeniedOfflineTier";

    /// <summary>The licence grace window has elapsed, so entitlements cannot be asserted.</summary>
    public const string FeatureDeniedGraceExpired = "FeatureDeniedGraceExpired";

    /// <summary>No session, so there is no licence to ask about.</summary>
    public const string FeatureDeniedSignIn = "FeatureDeniedSignIn";

    /// <summary>A plain upgrade denial. <c>{0}</c> feature label.</summary>
    public const string FeatureDeniedUpgradeRequired = "FeatureDeniedUpgradeRequired";

    /// <summary>FeatureSvc unreachable and the licence does not permit features.</summary>
    public const string FeatureDeniedUnverifiable = "FeatureDeniedUnverifiable";

    /// <summary><c>FEATURE_NOT_AVAILABLE</c>. <c>{0}</c> feature label.</summary>
    public const string FeatureDeniedNotInPlan = "FeatureDeniedNotInPlan";

    /// <summary><c>FEATURE_NOT_FOUND</c> / <c>FLAG_NOT_FOUND</c>. <c>{0}</c> feature label.</summary>
    public const string FeatureDeniedUnknownFeature = "FeatureDeniedUnknownFeature";

    /// <summary><c>LICENSE_EXPIRED</c> / <c>LICENSE_INACTIVE</c> on a feature check.</summary>
    public const string FeatureDeniedLicenseExpired = "FeatureDeniedLicenseExpired";

    /// <summary><c>RATE_LIMITED</c> on a feature check.</summary>
    public const string FeatureDeniedRateLimited = "FeatureDeniedRateLimited";

    /// <summary>
    /// The frame a verbatim AppManager sentence is shown inside. <c>{0}</c> is the server's own
    /// text, untranslated.
    /// </summary>
    public const string ServerSuppliedMessage = "LicenseServerSuppliedMessage";

    /// <summary>Gets the resource key for the label of an <see cref="InstanceMode"/>.</summary>
    /// <param name="mode">The resolved mode.</param>
    /// <returns>The key the licence card and the mode sentences both use.</returns>
    /// <remarks>
    /// Reuses the keys the licence card already shipped rather than minting parallel ones, so the
    /// badge and the sentence beneath it can never disagree about what "Team" is called in Hindi.
    /// </remarks>
    public static string ForMode(InstanceMode mode) => mode switch
    {
        InstanceMode.Team => "LicenseCardModeTeam",
        InstanceMode.Enterprise => "LicenseCardModeEnterprise",
        _ => "LicenseCardModeIndividual"
    };

    /// <summary>Gets the message key explaining why a licence validation was rejected.</summary>
    /// <param name="error">The typed error, mapped from the wire CODE by <see cref="AppManagerErrorMapper"/>.</param>
    /// <returns>A resource key. Never null, never English.</returns>
    public static string ForValidationFailure(AppManagerError error) => error switch
    {
        AppManagerError.LicenseExpired => LicenseErrorExpired,
        AppManagerError.LicenseNotFound => LicenseErrorNotFound,
        AppManagerError.LicenseInactive => LicenseErrorInactive,
        AppManagerError.CrossAppLicense or AppManagerError.AppIdMismatch => LicenseErrorCrossApplication,
        AppManagerError.NoAppAccess => LicenseErrorNoAppAccess,
        AppManagerError.RateLimited => LicenseErrorRateLimited,
        AppManagerError.Unauthorized
            or AppManagerError.InvalidToken
            or AppManagerError.SessionExpired
            or AppManagerError.ExpiredRefreshToken
            or AppManagerError.RevokedRefreshToken => LicenseErrorSessionExpired,
        AppManagerError.AccountDisabled or AppManagerError.AccountLocked => LicenseErrorAccountDisabled,
        _ => LicenseErrorGeneric
    };

    /// <summary>Gets the message key explaining why FeatureSvc refused a feature.</summary>
    /// <param name="error">The typed error, mapped from the wire CODE.</param>
    /// <returns>A resource key. Never null, never English.</returns>
    public static string ForFeatureFailure(AppManagerError error) => error switch
    {
        AppManagerError.FeatureNotAvailable => FeatureDeniedNotInPlan,
        AppManagerError.FeatureNotFound or AppManagerError.FlagNotFound => FeatureDeniedUnknownFeature,
        AppManagerError.LicenseExpired or AppManagerError.LicenseInactive => FeatureDeniedLicenseExpired,
        AppManagerError.RateLimited => FeatureDeniedRateLimited,
        AppManagerError.Unauthorized
            or AppManagerError.InvalidToken
            or AppManagerError.SessionExpired => FeatureDeniedSignIn,
        _ => FeatureDeniedUpgradeRequired
    };

    /// <summary>
    /// Every key this class can produce, so a test can prove the whole set resolves in both
    /// shipped languages rather than only the branches a test happened to drive.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        StateOffline,
        StateNotValidated,
        StateValidated,
        StateNoValidLicense,
        StateCached,
        StateNoCachedLicense,
        StateGraceExpired,
        LicenseErrorGeneric,
        LicenseErrorExpired,
        LicenseErrorNotFound,
        LicenseErrorInactive,
        LicenseErrorCrossApplication,
        LicenseErrorNoAppAccess,
        LicenseErrorRateLimited,
        LicenseErrorSessionExpired,
        LicenseErrorAccountDisabled,
        ModeOffline,
        ModeNotChecked,
        ModeServerUnreachable,
        ModeNoSeat,
        ModePersonal,
        ModePersonalOnPlan,
        ModePersonalInactive,
        ModePersonalInactiveOnPlan,
        ModeSeatAssigned,
        ModeSeatAssignedCached,
        ModeSeatExpired,
        ModeSeatRevoked,
        ModeSeatUnverified,
        ModeSeatUnassigned,
        ModeFloor,
        FeatureDeniedNoFeature,
        FeatureDeniedOfflineTier,
        FeatureDeniedGraceExpired,
        FeatureDeniedSignIn,
        FeatureDeniedUpgradeRequired,
        FeatureDeniedUnverifiable,
        FeatureDeniedNotInPlan,
        FeatureDeniedUnknownFeature,
        FeatureDeniedLicenseExpired,
        FeatureDeniedRateLimited,
        ServerSuppliedMessage,
        .. Enum.GetValues<InstanceMode>().Select(ForMode)
    ];
}
