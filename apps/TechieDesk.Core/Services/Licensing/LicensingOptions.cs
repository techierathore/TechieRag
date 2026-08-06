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
        "WHITE_LABEL",

        // REQ-FN-045/BRD-143: team seat features follow the WHITE_LABEL pattern exactly — a
        // tier-gated feature code, denied on the offline Free tier. Gating the CODE never gates
        // the user's own data (BRD-129); it only hides the paid team surface.
        "TEAM_SEATS"
    };

    /// <summary>The license tier suggested to unlock a gated feature when none is specified.</summary>
    public string DefaultUpgradeTier { get; set; } = "Professional";

    /// <summary>
    /// Whether <c>POST /LicenseSvc/validate</c> carries this installation's identity
    /// (config key <c>AppManager:SendInstallIdentity</c>, REQ-FN-051 clause 2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Defaulted OFF, and that is the finding, not an oversight.</b> Clause (2) needs AppManager
    /// to bind a seat to an install, and <c>docs/AppManager-api-usage-guide.md</c> documents no such
    /// contract: <c>POST /LicenseSvc/validate</c> is specified as taking NO request body, and the
    /// only device-shaped endpoint in the whole API is
    /// <c>DELETE /LicenseSvc/{aLicenseId}/devices/{aDeviceId}</c> — a DEACTIVATION that takes a
    /// server-issued integer id there is no documented way to obtain. Inventing a registration call
    /// is out of scope, so the client half is built and left dark.
    /// </para>
    /// <para>
    /// When turned on, the identity travels as the <c>X-Install-Id</c> request header rather than in
    /// a body, because adding a body to an endpoint documented as having none is the change that
    /// could break the contract, whereas an unrecognised header is discarded by any HTTP server. It
    /// is a hash (see <c>InstallIdentity.CompositeId</c>), so no hardware identifier goes on the wire
    /// (REQ-NFR-008). Turn it on only once AppManager is known to accept it — this could not be
    /// verified here, as no reachable AppManager exists on this host.
    /// </para>
    /// <para>
    /// <b>What the server side still needs — REQ-FN-051 clauses 2 and 4, deliberately NOT built.</b>
    /// Clause (4) ("revoking or reassigning a seat degrades the un-bound install at its next
    /// successful check") is unbuildable until AppManager can say whether THIS install is the one the
    /// seat is bound to, and today it cannot. Precisely what is missing:
    /// <list type="number">
    /// <item><description>
    /// <b>Registration.</b> <c>POST /LicenseSvc/validate</c> must accept the install identity —
    /// the <c>X-Install-Id</c> header sent here, or a documented body field — and bind it to the
    /// caller's seat on first sight, counting against <c>maxDevices</c>.
    /// </description></item>
    /// <item><description>
    /// <b>An answer in the response.</b> <c>LicenseValidationData</c> must gain a field saying
    /// whether the presented install is the bound one — e.g. <c>installBinding</c> ∈
    /// { <c>Bound</c>, <c>NotBound</c>, <c>Reassigned</c>, <c>SeatLimitExceeded</c> }. Without a
    /// field on the response there is nothing for a client to degrade ON: a validated licence
    /// currently looks identical whether the seat belongs to this install or to another one.
    /// </description></item>
    /// <item><description>
    /// <b>A way to enumerate and release.</b> The only device-shaped endpoint that exists is
    /// <c>DELETE /LicenseSvc/{aLicenseId}/devices/{aDeviceId}</c>, and there is no documented call
    /// that returns a <c>deviceId</c>, so an over-limit user cannot free a seat. A
    /// <c>GET /LicenseSvc/{aLicenseId}/devices</c> is required for the deactivation endpoint to be
    /// reachable at all.
    /// </description></item>
    /// </list>
    /// Only once (2) exists can the client degrade a NotBound install to Individual at its next
    /// successful check, reusing the REQ-FN-045 clause (3) path in <c>InstanceModeResolver</c> —
    /// degrade, never lock, and never touching local data (BRD-129).
    /// </para>
    /// </remarks>
    public bool SendInstallIdentity { get; set; }

    /// <summary>
    /// AppManager licence tier names that entitle <see cref="InstanceMode.Team"/>
    /// (REQ-FN-044/BRD-142). Configurable so the owner can rename a plan in AppManager without a
    /// code change. Additive only: a tier not listed here (and not in
    /// <see cref="EnterpriseLicenseTiers"/>) resolves to <see cref="InstanceMode.Individual"/>,
    /// which is fully usable — an unrecognised tier can never take capability away.
    /// </summary>
    public HashSet<string> TeamLicenseTiers { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Team",
        "Business"
    };

    /// <summary>
    /// AppManager licence tier names that entitle <see cref="InstanceMode.Enterprise"/>
    /// (REQ-FN-044/BRD-142). Checked before <see cref="TeamLicenseTiers"/>, so a name appearing
    /// in both resolves to the higher tier.
    /// </summary>
    public HashSet<string> EnterpriseLicenseTiers { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Enterprise"
    };
}
