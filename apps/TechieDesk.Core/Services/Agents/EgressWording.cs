using System.Globalization;
using System.Resources;
using TechieDesk.Resources;

namespace TechieDesk.Services.Agents;

/// <summary>
/// The single source the egress gate's wording is drawn from, so the gate and the screens can never
/// say different things about the same control (REQ-UI-055 / BRD-91, protecting REQ-NFR-013).
/// </summary>
/// <remarks>
/// <para><b>The defect this closes.</b> <see cref="EgressGate"/>'s refusal told the model to turn off
/// <i>"Ask before any skill that leaves this machine"</i> — a SECOND COPY of the switch label, typed
/// into a C# literal, while the switch itself renders <c>AgentsConfirmEgress</c> from the resources.
/// Two copies of one sentence drift the first time somebody rewords the switch, and the reader is
/// then told to turn off a control that no longer exists under that name. REQ-NFR-013 exists because
/// that control's promise and its behaviour disagreed once already; this makes the promise and the
/// quote of the promise the same bytes.</para>
/// <para><b>Two resolutions, because there are two audiences.</b>
/// <see cref="InEnglish"/> reads the NEUTRAL resource whatever culture the app is running in, and is
/// for text the MODEL consumes — a tool result, a refusal payload, anything that lands in the
/// conversation the LLM reasons over. A Hindi sentence in an English conversation degrades tool
/// calling and invites the model to answer in the wrong language, so model-facing text is pinned to
/// English by construction rather than by remembering. <see cref="ForReader"/> reads the reader's
/// culture and is for text only a PERSON sees — today the execution-trace row an
/// <see cref="TechieRag.Models.ToolResult.ErrorMessage"/> becomes, which
/// <c>AgentLoopRunner</c> never adds to the model's message list.</para>
/// <para><b>Why a <see cref="ResourceManager"/> and not <c>IStringLocalizer</c> or
/// <see cref="Localization.LocalizeText"/>.</b> Both of those are the right answer when a service can
/// hand a KEY to whatever renders it — which is what REQ-UI-051 established and what
/// <see cref="SkillCatalog"/> and <see cref="Flows.FlowGuardrailCatalog"/> do. Neither works here:
/// the two strings this type resolves are handed to a LIBRARY contract that requires a finished
/// sentence — <c>ToolResult.Content</c> is read by the model and <c>ToolResult.ErrorMessage</c> is
/// rendered verbatim by <see cref="AgentTrace"/> — so a key would surface as the literal text
/// <c>TraceMcpEgressNotApproved</c> on a screen. <see cref="Localization.LocalizeText"/>'s own remarks
/// name this case: the handful of places where the service owns the composition. A static
/// <see cref="ResourceManager"/> is what <c>ResourceManagerStringLocalizer</c> is built on, so this
/// resolves the identical value from the identical .resx entry with no DI to thread through
/// <c>WorkspaceMcpService</c> and no localizer parked on <see cref="EgressGate"/>.</para>
/// </remarks>
public static class EgressWording
{
    /// <summary>
    /// Resource key of the per-agent switch <see cref="EgressGate"/> enforces — the one the
    /// Guardrails tab binds and the one the gate quotes.
    /// </summary>
    public const string ConfirmEgressSettingKey = "AgentsConfirmEgress";

    /// <summary>
    /// Resource key of the execution-trace row shown when an outbound MCP tool call was not
    /// approved.
    /// </summary>
    public const string McpCallNotApprovedKey = "TraceMcpEgressNotApproved";

    /// <summary>
    /// The app's resource set, addressed exactly as <c>ResourceManagerStringLocalizerFactory</c>
    /// addresses it: the full name of <see cref="AppStrings"/> as the base name. See that type for
    /// why the folder must not be prepended a second time.
    /// </summary>
    private static readonly ResourceManager Strings =
        new("TechieDesk.Resources.AppStrings", typeof(AppStrings).Assembly);

    /// <summary>
    /// Resolves a key in ENGLISH, for text the model reads.
    /// </summary>
    /// <param name="key">A key present in <c>AppStrings.resx</c>.</param>
    /// <returns>The neutral-culture value, whatever culture the reader is in.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is blank.</exception>
    public static string InEnglish(string key) => Resolve(key, CultureInfo.InvariantCulture);

    /// <summary>
    /// Resolves a key in the READER's language, for text no model ever sees.
    /// </summary>
    /// <param name="key">A key present in <c>AppStrings.resx</c> and every shipped translation.</param>
    /// <returns>The value for <see cref="CultureInfo.CurrentUICulture"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is blank.</exception>
    public static string ForReader(string key) => Resolve(key, CultureInfo.CurrentUICulture);

    /// <summary>Reads one entry, falling back to the key rather than throwing.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="culture">The culture to read it in.</param>
    /// <returns>The value, or the key when the resource set does not carry it.</returns>
    /// <remarks>
    /// Returning the key is what <c>ResourceManagerStringLocalizer</c> does for a miss, and the
    /// tests that hold these keys are the ones that make a miss impossible. Throwing here would turn
    /// a missing translation into a failed agent turn, which is a far worse outcome than a visible
    /// key.
    /// </remarks>
    private static string Resolve(string key, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return Strings.GetString(key, culture) ?? key;
    }
}
