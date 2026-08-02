using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Licensing;

/// <summary>
/// The access decision for a single feature (REQ-FN-014/BRD-50). Carries the granted level for
/// level-type features and the tier required to unlock a denied feature (drives the upgrade prompt).
/// </summary>
/// <param name="FeatureCode">The feature code (e.g. <c>CONNECTORS</c>). Culture-invariant wire vocabulary.</param>
/// <param name="IsEnabled">Whether the current user may use the feature.</param>
/// <param name="Level">The granted level for level-type features, when applicable.</param>
/// <param name="RequiredLicense">
/// The license tier that unlocks a denied feature, when known. Culture-invariant: it is either
/// AppManager's own <c>requiredLicense</c> or <see cref="LicensingOptions.DefaultUpgradeTier"/>,
/// both of which name a plan the licence server matches by name.
/// </param>
/// <param name="ReasonKey">
/// Resource key for OUR OWN explanation of a denial (REQ-UI-055), or null when the only
/// explanation is <paramref name="ServerReason"/>.
/// </param>
/// <param name="ReasonArguments">Format arguments for <paramref name="ReasonKey"/>.</param>
/// <param name="ServerReason">
/// A denial sentence supplied VERBATIM by AppManager's FeatureSvc, or null. See
/// <see cref="DescribeReason"/> for how it is presented and why it is not translated.
/// </param>
public sealed record FeatureDecision(
    string FeatureCode,
    bool IsEnabled,
    int? Level = null,
    string? RequiredLicense = null,
    string? ReasonKey = null,
    IReadOnlyList<object?>? ReasonArguments = null,
    string? ServerReason = null)
{
    /// <summary>Creates an allowed decision, optionally carrying a granted level.</summary>
    public static FeatureDecision Allowed(string featureCode, int? level = null)
        => new(featureCode, true, level);

    /// <summary>Creates a denied decision explained by one of this app's own resource keys.</summary>
    /// <param name="featureCode">The feature code that was refused.</param>
    /// <param name="requiredLicense">The tier that would unlock it, when known.</param>
    /// <param name="reasonKey">A key from <see cref="LicenseMessageKeys"/>.</param>
    /// <param name="reasonArguments">Format arguments for the key.</param>
    /// <returns>The denial.</returns>
    public static FeatureDecision Denied(
        string featureCode,
        string? requiredLicense = null,
        string? reasonKey = null,
        IReadOnlyList<object?>? reasonArguments = null)
        => new(featureCode, false, null, requiredLicense, reasonKey, reasonArguments);

    /// <summary>Creates a denied decision whose only explanation came from the licence server.</summary>
    /// <param name="featureCode">The feature code that was refused.</param>
    /// <param name="requiredLicense">The tier that would unlock it, when known.</param>
    /// <param name="serverReason">FeatureSvc's own sentence, exactly as it arrived.</param>
    /// <returns>The denial.</returns>
    public static FeatureDecision DeniedByServer(
        string featureCode, string? requiredLicense, string serverReason)
        => new(featureCode, false, null, requiredLicense, ServerReason: serverReason);

    /// <summary>
    /// Renders the denial reason in the reader's language, or frames the server's own words.
    /// </summary>
    /// <param name="localize">The renderer's localizer, e.g. <c>(k, a) =&gt; Localizer[k, a!].Value</c>.</param>
    /// <returns>The sentence to show, or the empty string when there is no reason to give.</returns>
    /// <remarks>
    /// <para>
    /// <b>The server-supplied case, and why it is framed rather than translated (REQ-UI-055).</b>
    /// <c>FeatureSvc</c> may return a <c>reason</c> written by whoever configured the feature in
    /// AppManager. It arrives at run time, in whatever language that operator wrote it in — in
    /// practice English — and there is nothing to look up: no key exists for a sentence this build
    /// has never seen. Matching it against known English would be the exact defect REQ-UI-055 is
    /// about, and dropping it would throw away the only text that says why THIS deployment refused
    /// THIS feature.
    /// </para>
    /// <para>
    /// So it is shown, inside a localized frame — <see cref="LicenseMessageKeys.ServerSuppliedMessage"/>,
    /// "The licence server said: {0}". The frame is the honest part: a Hindi reader gets a Hindi
    /// sentence telling them the quoted words are the server's and not the app's, instead of a bare
    /// English fragment that reads as a missed translation.
    /// </para>
    /// </remarks>
    public string DescribeReason(LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        return string.IsNullOrWhiteSpace(ServerReason)
            ? LicenseMessage.Resolve(localize, ReasonKey, ReasonArguments)
            : localize(LicenseMessageKeys.ServerSuppliedMessage, ServerReason);
    }
}
