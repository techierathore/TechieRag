namespace TechieDesk.Services.Licensing;

/// <summary>
/// Licensing configuration, bound from the <c>AppManager</c> configuration section so the
/// documented key names (<c>AppManager:LicenseGraceHours</c>) sit alongside the AppManager
/// credentials. Governs the outage grace window (REQ-FN-015/BRD-51), how often the license
/// is re-validated (REQ-FN-013/BRD-49), and which feature codes are treated as premium in
/// offline single-user mode (REQ-FN-014/BRD-50).
/// </summary>
public sealed class LicensingOptions
{
    /// <summary>Name of the configuration section this options class binds to.</summary>
    public const string SectionName = "AppManager";

    /// <summary>
    /// Hours the last-known-good license is honored after AppManager becomes unreachable
    /// (config key <c>AppManager:LicenseGraceHours</c>, REQ-FN-015). After this window the
    /// instance degrades: premium features lock and a clear message is shown.
    /// </summary>
    public int LicenseGraceHours { get; set; } = 72;

    /// <summary>
    /// Minutes between automatic license re-validations. A navigation (or a status read) after
    /// this interval elapses triggers a fresh <c>POST /LicenseSvc/validate</c> (REQ-FN-013).
    /// </summary>
    public int LicenseRevalidationMinutes { get; set; } = 60;

    /// <summary>
    /// Feature codes considered premium in offline single-user mode. Offline instances run the
    /// Free tier (see the pricing mockup: no connectors/agents/API/embed/white-label), so these
    /// codes are gated and show an upgrade prompt even without AppManager. AppManager mode never
    /// consults this list — it asks FeatureSvc directly.
    /// </summary>
    public HashSet<string> OfflinePremiumFeatures { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "CONNECTORS",
        "AGENTS",
        "API_ACCESS",
        "EMBED_WIDGET",
        "WHITE_LABEL"
    };

    /// <summary>The license tier suggested to unlock a gated feature when none is specified.</summary>
    public string DefaultUpgradeTier { get; set; } = "Professional";
}
