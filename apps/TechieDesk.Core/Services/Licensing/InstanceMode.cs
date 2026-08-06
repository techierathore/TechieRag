namespace TechieDesk.Services.Licensing;

/// <summary>
/// The licensing mode of this install, resolved from the AppManager licence tier
/// (REQ-FN-044 / BRD-142).
/// <para>
/// <b>Scope, stated so it is not drifted into later (ADR-012):</b> the mode affects
/// <i>entitlements and the visibility of team features only</i>. It never makes the install
/// multi-user, never partitions data between users, never introduces roles or a capability
/// matrix, and <b>never gates the user's own local data</b>. A team is N independent
/// single-user installs joined by AppManager seats — never one shared instance.
/// </para>
/// <para>
/// <see cref="Individual"/> is the <b>floor</b>, not a failure state. Every degradation path —
/// unassigned, expired, revoked, unreachable or unverifiable seat — lands here with full local
/// capability intact (BRD-129). There is deliberately no locked, read-only or nagging member.
/// </para>
/// </summary>
public enum InstanceMode
{
    /// <summary>
    /// A personal licence, no licence at all, or any degraded seat. Full capability over the
    /// user's own local data; paid features are gated by <see cref="IFeatureGate"/> as usual.
    /// </summary>
    Individual = 0,

    /// <summary>An assigned, active organisation seat on a Team-tier licence (BRD-143).</summary>
    Team,

    /// <summary>An assigned, active organisation seat on an Enterprise-tier licence (BRD-143).</summary>
    Enterprise
}

/// <summary>
/// How this install's AppManager seat currently stands (REQ-FN-045 / BRD-143). Purely
/// informational plus entitlement-bearing: no member of this enum removes access to local data.
/// </summary>
public enum SeatState
{
    /// <summary>
    /// No organisation seat is involved — a personal licence, or offline single-user mode.
    /// This is the ordinary Individual state, not a problem to report.
    /// </summary>
    None = 0,

    /// <summary>The signed-in user holds an assigned, active seat — Team/Enterprise entitlements apply.</summary>
    Assigned,

    /// <summary>
    /// A team-tier licence exists but no seat is assigned to this user. Degrades to
    /// <see cref="InstanceMode.Individual"/> capability, never to a locked state.
    /// </summary>
    Unassigned,

    /// <summary>The seat's licence has expired. Degrades to Individual capability.</summary>
    Expired,

    /// <summary>
    /// The seat was revoked, cancelled or suspended by the organisation. Degrades to Individual
    /// capability at the next successful check — it does not lock the install.
    /// </summary>
    Revoked,

    /// <summary>
    /// The seat could not be verified — AppManager is unreachable and the BRD-51 grace window has
    /// elapsed, or the licence answer was not conclusive. Degrades to Individual capability.
    /// </summary>
    Unverified
}
