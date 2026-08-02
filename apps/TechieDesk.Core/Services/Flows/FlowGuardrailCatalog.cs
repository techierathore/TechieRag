using System.Text.RegularExpressions;
using TechieDesk.Services.Agents;
using TechieRag.Orchestration;

namespace TechieDesk.Services.Flows;

/// <summary>
/// The guardrails a flow AUTHOR may attach to a node, and the <see cref="IFlowGuardrailResolver"/>
/// that binds the ids they choose (REQ-UI-040 / BRD-92).
/// </summary>
/// <remarks>
/// <para><b>These are the author's business rules, not the host's security boundary.</b> The host's
/// own non-negotiable checks — the REQ-NFR-013 egress gate — live in
/// <see cref="FlowRuntime.HostGuardrails"/>, are supplied at run time, and cannot be named, added or
/// removed by editing a flow. Everything here is opt-in per node and removing it from a node removes
/// it. Keeping the two sets physically separate is what makes that sentence true rather than a
/// convention.</para>
/// <para><b>Ids are stable, English-free strings and go into the stored document.</b> They are
/// persisted inside <see cref="FlowNode.GuardrailIds"/>, so they are wire values. The screen
/// translates them by id (TR-024: a translated string must never become a bound value); the
/// <see cref="IFlowGuardrail.Description"/> here is the English fallback the library puts in a trace.</para>
/// <para><b>An id this catalogue does not know resolves to null, which BLOCKS.</b> That is the
/// library's deny-by-default rule and it is the correct behaviour: a flow naming a check this build
/// does not have must not run as though the check had passed.</para>
/// </remarks>
public sealed class FlowGuardrailCatalog : IFlowGuardrailResolver
{
    /// <summary>The id of the local-tools-only guardrail.</summary>
    public const string LocalToolsOnlyId = "local-tools-only";

    /// <summary>The id of the non-empty-output guardrail.</summary>
    public const string NonEmptyOutputId = "non-empty-output";

    /// <summary>The id of the no-credentials-in-output guardrail.</summary>
    public const string NoCredentialsInOutputId = "no-credentials-in-output";

    /// <summary>Resource key for the reason shown when <see cref="NonEmptyOutputId"/> refuses.</summary>
    public const string NonEmptyOutputBlockReasonKey = "FlowsGuardrailBlockedNonEmptyOutput";

    /// <summary>Resource key for the reason shown when <see cref="NoCredentialsInOutputId"/> refuses.</summary>
    public const string NoCredentialsBlockReasonKey = "FlowsGuardrailBlockedNoCredentials";

    private static readonly Regex CredentialPattern = new(
        @"(sk-[A-Za-z0-9]{16,})|(gh[pousr]_[A-Za-z0-9]{20,})|(AKIA[0-9A-Z]{16})|(-----BEGIN [A-Z ]*PRIVATE KEY-----)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(250));

    private readonly Dictionary<string, IFlowGuardrail> guardrails;

    /// <summary>Creates the catalogue.</summary>
    public FlowGuardrailCatalog()
    {
        IFlowGuardrail[] all =
        [
            new DelegateFlowGuardrail(
                LocalToolsOnlyId,
                "Local tools only: refuses any tool this build does not know to run on this machine.",
                [GuardrailStage.ToolCall],
                (context, _) => Task.FromResult(IsKnownLocalTool(context.ToolName)
                    ? GuardrailDecision.Allow()
                    : GuardrailDecision.Block(
                        $"'{context.ToolName}' is not a built-in skill known to run on this machine, and this "
                        + "node is restricted to local tools."))),

            new DelegateFlowGuardrail(
                NonEmptyOutputId,
                "Requires a non-empty answer: refuses a step that produced nothing.",
                [GuardrailStage.Output],
                (context, _) => Task.FromResult(string.IsNullOrWhiteSpace(context.Payload)
                    ? GuardrailDecision.Block("The step produced no output, and this node requires one.")
                    : GuardrailDecision.Allow())),

            new DelegateFlowGuardrail(
                NoCredentialsInOutputId,
                "Refuses output that carries something shaped like an API key, token or private key.",
                [GuardrailStage.Output],
                (context, _) => Task.FromResult(LooksLikeCredential(context.Payload)
                    ? GuardrailDecision.Block(
                        "The step's output contains something shaped like a credential, so it was not passed on.")
                    : GuardrailDecision.Allow()))
        ];

        guardrails = all.ToDictionary(guardrail => guardrail.Id, StringComparer.Ordinal);
        Available = all;
    }

    /// <summary>Gets every guardrail a node editor may offer, in palette order.</summary>
    public IReadOnlyList<IFlowGuardrail> Available { get; }

    /// <inheritdoc />
    public Task<IFlowGuardrail?> ResolveGuardrailAsync(
        string guardrailId, CancellationToken cancellationToken = default) =>
        Task.FromResult(guardrailId is not null && guardrails.TryGetValue(guardrailId, out var guardrail)
            ? guardrail
            : null);

    /// <inheritdoc />
    public Task<IReadOnlyList<IFlowGuardrail>> ListGuardrailsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Available);

    /// <summary>
    /// Gets the resource key for the localized reason a guardrail refusal shows the USER, or null
    /// when this build has no translated wording for that id.
    /// </summary>
    /// <param name="guardrailId">The id from <c>FlowRunResult.BlockedByGuardrailId</c>.</param>
    /// <returns>A key in <c>AppStrings.resx</c>, or null to fall back to the English reason.</returns>
    /// <remarks>
    /// <para><b>REQ-UI-055: why a key here and an English sentence in the decision.</b> A block
    /// reason has two readers and they need different things. The reason carried on
    /// <see cref="GuardrailDecision"/> reaches the MODEL — <c>AgentToolHandler</c> puts
    /// <c>result.BlockReason</c> into the <c>unavailable:</c> tool result when a flow is invoked as a
    /// tool by an agent — and it reaches the library's own trace and logs, so it must stay a finished
    /// ENGLISH sentence. The same reason is also painted at <c>/workspaces/…/flows</c> inside
    /// <c>FlowsRunBlockedBy</c>, where an English clause in a Hindi alert is the defect REQ-UI-051
    /// was raised over. The id is the wire value that survives both, so the screen translates BY ID
    /// and the payload stays English — the same two-ways-to-name-one-thing shape
    /// <see cref="Agents.EgressConfirmationRequest"/> documents, and the same shape
    /// <see cref="Agents.SkillCatalog.ExposureLabelKey"/> uses.</para>
    /// <para><b>Only the OUTPUT-stage rails have a key, deliberately.</b> A refusal becomes
    /// <c>FlowRunResult.BlockReason</c> — the value the screen renders — only when it stopped the
    /// run, which is what an <see cref="GuardrailStage.Output"/> refusal does.
    /// <see cref="LocalToolsOnlyId"/> refuses at <see cref="GuardrailStage.ToolCall"/>: the run
    /// carries on, its reason goes back to the model as a tool result and never onto that alert, so
    /// giving it a key would ship a translation nothing can render.</para>
    /// </remarks>
    public static string? BlockReasonKey(string? guardrailId) => guardrailId switch
    {
        NonEmptyOutputId => NonEmptyOutputBlockReasonKey,
        NoCredentialsInOutputId => NoCredentialsBlockReasonKey,
        _ => null
    };

    /// <summary>
    /// Decides whether a tool name is one this build knows runs entirely on this machine.
    /// </summary>
    /// <param name="toolName">The tool the model or the node asked for.</param>
    /// <returns>True only for a catalogue skill whose exposure is <see cref="SkillExposure.Local"/>.</returns>
    /// <remarks>
    /// Deny by default, and deliberately so. An MCP tool registered by an administrator is not in
    /// <see cref="SkillCatalog"/> and this method has no way to know its server's transport, so
    /// "unknown" is treated as "not local" rather than guessed from the name. A guardrail that
    /// allowed anything it did not recognise would be decorative.
    /// </remarks>
    private static bool IsKnownLocalTool(string? toolName) =>
        SkillCatalog.Find(toolName)?.Exposure == SkillExposure.Local;

    /// <summary>Applies the credential-shaped-text check with a bounded regular expression.</summary>
    /// <param name="payload">The step output under inspection.</param>
    /// <returns>True when the text carries something shaped like a credential.</returns>
    private static bool LooksLikeCredential(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            return CredentialPattern.IsMatch(payload);
        }
        catch (RegexMatchTimeoutException)
        {
            // A timeout on a pathological payload is not permission to pass it on: this guardrail
            // exists to refuse, so an inconclusive answer refuses.
            return true;
        }
    }
}
