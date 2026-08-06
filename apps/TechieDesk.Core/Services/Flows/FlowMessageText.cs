using TechieDesk.Services.Localization;
using TechieRag.Orchestration;

namespace TechieDesk.Services.Flows;

/// <summary>
/// Renders the library's <see cref="FlowMessage"/> codes in the reader's language (REQ-UI-058 /
/// BRD-91).
/// </summary>
/// <remarks>
/// <para><b>This is the reader <c>REQ-RAG-050</c> was missing.</b> The library was taught to emit a
/// stable code plus its arguments instead of a finished English sentence — and then nothing consumed
/// it, so a Hindi user still met English at the exact moment a flow refused something. The library
/// half was done, tested, and invisible. This type is the half that reaches a person.</para>
/// <para><b>The library cannot do this itself, and should not try.</b> TechieRag is a redistributable
/// package with no resource files and no idea what languages its host ships; the consumer owns the
/// wording. That is the same split <c>FlowValidationCodes</c> already keeps, and
/// <see cref="FlowMessage.Text"/> stays available as the fallback for a code this app has not
/// learned yet.</para>
/// <para><b>An unknown code degrades to English rather than to a blank or a token.</b> A library
/// upgrade that adds a code must not empty a trace row — the reader sees the library's own English
/// until a resource key catches up, which is strictly better than the alternatives.</para>
/// </remarks>
public static class FlowMessageText
{
    /// <summary>
    /// Maps every <see cref="FlowMessageCodes"/> member to its <c>AppStrings</c> key.
    /// </summary>
    /// <remarks>
    /// Exhaustive by test: <c>FlowMessageLocalizationCoverageTests</c> reflects over
    /// <see cref="FlowMessageCodes"/> and fails when a member has no entry here, so a library upgrade
    /// that adds a code cannot silently ship as English. The keys are prefixed <c>FlowMsg</c> so they
    /// are recognisable as library-originated wording rather than screen copy.
    /// </remarks>
    private static readonly Dictionary<string, string> ResourceKeys = new(StringComparer.Ordinal)
    {
        [FlowMessageCodes.GuardrailRefusedCall] = "FlowMsgGuardrailRefusedCall",
        [FlowMessageCodes.GuardrailReturnedNoDecision] = "FlowMsgGuardrailReturnedNoDecision",
        [FlowMessageCodes.GuardrailFaulted] = "FlowMsgGuardrailFaulted",
        [FlowMessageCodes.GuardrailResolverMissing] = "FlowMsgGuardrailResolverMissing",
        [FlowMessageCodes.GuardrailUnresolvable] = "FlowMsgGuardrailUnresolvable",
        [FlowMessageCodes.BlockedByGuardrail] = "FlowMsgBlockedByGuardrail",
        [FlowMessageCodes.NodeBlockedByGuardrail] = "FlowMsgNodeBlockedByGuardrail",
        [FlowMessageCodes.ToolCallBlockedByGuardrail] = "FlowMsgToolCallBlockedByGuardrail",
        [FlowMessageCodes.ToolCallRefusedByGuardrail] = "FlowMsgToolCallRefusedByGuardrail",
        [FlowMessageCodes.FlowNotValidated] = "FlowMsgFlowNotValidated",
        [FlowMessageCodes.StepBudgetExhausted] = "FlowMsgStepBudgetExhausted",
        [FlowMessageCodes.StepBudgetReached] = "FlowMsgStepBudgetReached",
        [FlowMessageCodes.AgentUnavailable] = "FlowMsgAgentUnavailable",
        [FlowMessageCodes.AgentUnresolvable] = "FlowMsgAgentUnresolvable",
        [FlowMessageCodes.AgentStepFailed] = "FlowMsgAgentStepFailed",
        [FlowMessageCodes.ToolHandlerMissing] = "FlowMsgToolHandlerMissing",
        [FlowMessageCodes.NoToolHandlerConfigured] = "FlowMsgNoToolHandlerConfigured",
        [FlowMessageCodes.RouteToNode] = "FlowMsgRouteToNode",
        [FlowMessageCodes.HandoffNoVariables] = "FlowMsgHandoffNoVariables",
        [FlowMessageCodes.HandoffCarryingVariables] = "FlowMsgHandoffCarryingVariables",
        [FlowMessageCodes.SubFlowBlocked] = "FlowMsgSubFlowBlocked",
        [FlowMessageCodes.SubFlowStepBudgetExhausted] = "FlowMsgSubFlowStepBudgetExhausted",
        [FlowMessageCodes.SubFlowFailed] = "FlowMsgSubFlowFailed",
        [FlowMessageCodes.SubFlowInvocationLimitReached] = "FlowMsgSubFlowInvocationLimitReached"
    };

    /// <summary>
    /// The codes whose LAST argument is another guardrail's reason rather than plain data.
    /// </summary>
    /// <remarks>
    /// <para><b>The nested-code substitution the requirement names.</b> A refusal is always "this
    /// framing, around that guardrail's reason". The framing is the library's and carries a code; the
    /// reason belongs to the GUARDRAIL, and for a catalogue guardrail this app already owns its
    /// wording under <see cref="FlowGuardrailCatalog.BlockReasonKey"/>. Without this the outer
    /// sentence would translate while the clause a user actually reads — <i>why</i> it was refused —
    /// stayed English, which is the defect half-fixed rather than fixed.</para>
    /// <para>A HOST guardrail that supplied a plain string carries no key; its reason stays as it came,
    /// which is the host's own choice to make.</para>
    /// </remarks>
    private static readonly Dictionary<string, int> NestedReasonArgument = new(StringComparer.Ordinal)
    {
        [FlowMessageCodes.ToolCallRefusedByGuardrail] = 1,
        [FlowMessageCodes.SubFlowBlocked] = 2
    };

    /// <summary>The guardrail-id argument position, for the codes that carry one.</summary>
    private static readonly Dictionary<string, int> GuardrailIdArgument = new(StringComparer.Ordinal)
    {
        [FlowMessageCodes.ToolCallRefusedByGuardrail] = 0,
        [FlowMessageCodes.SubFlowBlocked] = 1
    };

    /// <summary>Gets the <c>AppStrings</c> key for a library code, or null when it is unknown.</summary>
    /// <param name="code">A <see cref="FlowMessageCodes"/> value.</param>
    /// <returns>The resource key, or null.</returns>
    /// <remarks>
    /// A LIBRARY code is translated through the table above. An APP-authored code (a skill's
    /// unavailability, REQ-UI-059 clause 3) already IS an <c>AppStrings</c> key, so it maps to
    /// itself — one resolver serves both rather than the trace having to know which produced a
    /// message.
    /// </remarks>
    public static string? ResourceKey(string? code) =>
        code is null ? null
        : ResourceKeys.TryGetValue(code, out var key) ? key
        : code.StartsWith("SkillUnavailable", StringComparison.Ordinal) ? code
        : null;

    /// <summary>Gets every code this app can render, for the coverage test.</summary>
    public static IReadOnlyCollection<string> KnownCodes => ResourceKeys.Keys;

    /// <summary>
    /// Renders a library message in the reader's language, falling back to the stored English.
    /// </summary>
    /// <param name="message">The library's coded message, or null when it produced none.</param>
    /// <param name="fallback">What to show when there is no message — usually the English column beside it.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The sentence to show, or null when there is nothing to say.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="localize"/> is null.</exception>
    /// <remarks>
    /// The order matters: a coded message wins, an unknown code falls back to the library's own
    /// English wording (<see cref="FlowMessage.Text"/>), and only a message-less value falls through
    /// to <paramref name="fallback"/>. Each step degrades to something a person can read.
    /// </remarks>
    public static string? Resolve(FlowMessage? message, string? fallback, LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        if (message is null)
        {
            return fallback;
        }

        var key = ResourceKey(message.Code);
        if (key is null)
        {
            // A code this build has not learned. The library's English is better than a blank row or
            // a raw token, and the coverage test exists so this stays a theoretical branch.
            return message.Text;
        }

        var arguments = Substitute(message, localize);

        return arguments.Count == 0
            ? localize(key)
            : localize(key, [.. arguments.Cast<object?>()]);
    }

    /// <summary>
    /// Replaces a nested guardrail reason with its own localized wording, where this app owns one.
    /// </summary>
    /// <param name="message">The library message.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The arguments to format the outer sentence with.</returns>
    private static IReadOnlyList<string> Substitute(FlowMessage message, LocalizeText localize)
    {
        if (!NestedReasonArgument.TryGetValue(message.Code, out var reasonIndex)
            || !GuardrailIdArgument.TryGetValue(message.Code, out var idIndex)
            || message.Arguments.Count <= reasonIndex
            || message.Arguments.Count <= idIndex)
        {
            return message.Arguments;
        }

        var reasonKey = FlowGuardrailCatalog.BlockReasonKey(message.Arguments[idIndex]);
        if (reasonKey is null)
        {
            // A host guardrail that gave a plain string. Its words are not ours to replace.
            return message.Arguments;
        }

        var substituted = message.Arguments.ToArray();
        substituted[reasonIndex] = localize(reasonKey);
        return substituted;
    }
}
