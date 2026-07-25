namespace TechieDesk.Services.Licensing;

/// <summary>
/// The access decision for a single feature (REQ-FN-014/BRD-50). Carries the granted level for
/// level-type features and the tier required to unlock a denied feature (drives the upgrade prompt).
/// </summary>
/// <param name="FeatureCode">The feature code (e.g. <c>CONNECTORS</c>).</param>
/// <param name="IsEnabled">Whether the current user may use the feature.</param>
/// <param name="Level">The granted level for level-type features, when applicable.</param>
/// <param name="RequiredLicense">The license tier that unlocks a denied feature, when known.</param>
/// <param name="Reason">A human-readable reason, shown on the upgrade prompt when denied.</param>
public sealed record FeatureDecision(
    string FeatureCode,
    bool IsEnabled,
    int? Level = null,
    string? RequiredLicense = null,
    string? Reason = null)
{
    /// <summary>Creates an allowed decision, optionally carrying a granted level.</summary>
    public static FeatureDecision Allowed(string featureCode, int? level = null)
        => new(featureCode, true, level);

    /// <summary>Creates a denied decision with the tier that would unlock it.</summary>
    public static FeatureDecision Denied(string featureCode, string? requiredLicense = null, string? reason = null)
        => new(featureCode, false, null, requiredLicense, reason);
}
