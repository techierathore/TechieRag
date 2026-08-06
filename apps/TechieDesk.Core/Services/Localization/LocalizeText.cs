namespace TechieDesk.Services.Localization;

/// <summary>
/// Resolves a resource key into the reader's language (REQ-UI-051 / BRD-91).
/// </summary>
/// <param name="key">A key present in <c>AppStrings.resx</c>.</param>
/// <param name="arguments">Format arguments for a key that carries placeholders.</param>
/// <returns>The translated text.</returns>
/// <remarks>
/// <para>
/// <b>Why a delegate and not <c>IStringLocalizer</c>.</b> The types this exists for —
/// <see cref="Agents.AgentTrace"/> and the catalogue tables beside it — are static or are
/// constructed by non-UI callers (the agent tool planner, <c>TechieRagManager</c>,
/// <c>TechieRagConfigService</c>). Making them instance services purely so they could hold a
/// localizer would push a UI concern into every one of those callers. A delegate keeps the
/// dependency at the ONE call that actually renders, which is always a razor component that
/// already injects <c>IStringLocalizer&lt;AppStrings&gt;</c>: <c>(key, args) =&gt; Localizer[key, args!]</c>.
/// </para>
/// <para>
/// It is used sparingly and on purpose. The rule REQ-UI-051 establishes is that a service returns
/// a resource KEY, never an English sentence; this delegate is only for the handful of places
/// where the service also owns the composition (the trace's plain-text export, a fallback that has
/// to choose between a key and a server-supplied value) and handing the caller a bare key would
/// just move that composition into three pages instead of one.
/// </para>
/// </remarks>
public delegate string LocalizeText(string key, params object?[] arguments);
