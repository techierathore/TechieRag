using System.Globalization;

namespace TechieRag.Orchestration;

/// <summary>
/// A user-visible sentence a flow run produced, carried as a stable code plus its runtime
/// arguments, with the English wording as a fallback (REQ-RAG-050 / BRD-91).
/// </summary>
/// <remarks>
/// <para><b>Why this exists at all.</b> <see cref="FlowRunner"/>, <see cref="FlowGuardrailChain"/>
/// and <see cref="GuardedToolHandler"/> produce text a person reads on a screen — the detail line of
/// a trace row, and the alert that says why a run stopped. TechieRag is a redistributable library
/// with no UI and no resource files, so it cannot translate that text itself; but shipping only
/// English means a Hindi user meets an English refusal at the exact moment something went wrong.
/// The way out is the one this codebase already chose for validation: the library emits a code, the
/// consumer owns the wording.</para>
/// <para><b>It is the same contract <see cref="FlowValidationIssue"/> already keeps.</b> There, the
/// <see cref="FlowValidationIssue.Code"/> is the contract and
/// <see cref="FlowValidationIssue.Message"/> is a fallback, and TechieDesk switches on all 26
/// <see cref="FlowValidationCodes"/> members to pick a translated string. This type extends that
/// proven shape to run time rather than inventing a second mechanism, and adds the one thing
/// validation did not need: <see cref="Arguments"/>. A code alone cannot render "Blocked by
/// guardrail 'x' before 'y' ran" — the tool name and the guardrail id have to travel with it, in
/// order, so a translator can put them anywhere in the sentence.</para>
/// <para><b>The arguments are invariant values, never translated text.</b> Tool names, guardrail
/// ids, node names, agent ids and counts. That is the REQ-UI-051 rule — a service returns invariant
/// keys and the screen composes — applied one layer further down, and it is why
/// <see cref="Text"/> is formatted with <see cref="CultureInfo.InvariantCulture"/>.</para>
/// <para><b>Model-facing text is deliberately NOT carried here.</b> The <c>unavailable: …</c>
/// content of a blocked <c>ToolResult</c> is read by the LLM so it can adapt and finish its turn;
/// it stays a finished English sentence, exactly as TechieDesk's <c>FlowGuardrailCatalog</c>
/// documents. Only what reaches a <see cref="FlowStep"/> or a <see cref="FlowRunResult"/> — the two
/// things a UI renders — carries a code.</para>
/// </remarks>
public sealed record FlowMessage
{
    /// <summary>Gets the stable machine-readable code, from <see cref="FlowMessageCodes"/>.</summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the composite format string the English <see cref="Text"/> was rendered from, with
    /// indexed placeholders matching <see cref="Arguments"/>.
    /// </summary>
    /// <remarks>
    /// Exposed so a consumer without a translation for a code can still render the sentence, and so
    /// a test can prove every placeholder has an argument.
    /// </remarks>
    public required string Format { get; init; }

    /// <summary>
    /// Gets the runtime values the sentence names, in placeholder order. Invariant identifiers and
    /// counts only — never text that would itself need translating.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Gets the English wording, for a consumer that has no translation for <see cref="Code"/>.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Builds a message from a code, its English composite format, and its arguments.
    /// </summary>
    /// <param name="code">A stable code, normally from <see cref="FlowMessageCodes"/>.</param>
    /// <param name="format">The English wording, with <c>{0}</c>-style placeholders.</param>
    /// <param name="arguments">The runtime values, in placeholder order.</param>
    /// <returns>The message, with <see cref="Text"/> already rendered.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> or <paramref name="format"/> is blank.</exception>
    /// <remarks>
    /// Public so a HOST can mint messages for its own guardrails under its own codes and have them
    /// travel the same path as the library's — which is what makes this one mechanism rather than
    /// two.
    /// </remarks>
    public static FlowMessage Create(string code, string format, params string[]? arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        var values = arguments ?? [];

        return new FlowMessage
        {
            Code = code,
            Format = format,
            Arguments = values,
            Text = values.Length == 0
                ? format
                : string.Format(CultureInfo.InvariantCulture, format, values)
        };
    }

    /// <summary>Returns the English wording, so an existing <c>$"{message}"</c> site keeps working.</summary>
    /// <returns><see cref="Text"/>.</returns>
    public override string ToString() => Text;
}

/// <summary>
/// The stable codes for every user-visible sentence a flow run produces (REQ-RAG-050 / BRD-91).
/// </summary>
/// <remarks>
/// <para>The direct counterpart of <see cref="FlowValidationCodes"/>, which covers a flow before it
/// runs; this covers a flow while it runs. A consumer switches on these to pick translated wording,
/// and the arguments documented on each member are what its placeholders receive.</para>
/// <para><b>Adding a code is adding a translation obligation.</b> A consumer that does not know a
/// code falls back to <see cref="FlowMessage.Text"/> and shows English, so a new code degrades
/// rather than crashes — but it degrades to the defect this REQ exists to remove.</para>
/// </remarks>
public static class FlowMessageCodes
{
    /// <summary>A guardrail refused a tool call and gave no reason of its own. No arguments.</summary>
    public const string GuardrailRefusedCall = "GuardrailRefusedCall";

    /// <summary>A guardrail returned null instead of a decision, so the step was denied. No arguments.</summary>
    public const string GuardrailReturnedNoDecision = "GuardrailReturnedNoDecision";

    /// <summary>A guardrail threw, so the step was denied. <c>{0}</c> exception type, <c>{1}</c> exception message.</summary>
    public const string GuardrailFaulted = "GuardrailFaulted";

    /// <summary>The flow names a guardrail but the host configured no resolver at all. <c>{0}</c> guardrail id.</summary>
    public const string GuardrailResolverMissing = "GuardrailResolverMissing";

    /// <summary>The flow names a guardrail the host's resolver could not produce. <c>{0}</c> guardrail id.</summary>
    public const string GuardrailUnresolvable = "GuardrailUnresolvable";

    /// <summary>A step was refused and the refusal carried no reason. No arguments.</summary>
    public const string BlockedByGuardrail = "BlockedByGuardrail";

    /// <summary>A node's input or output was refused. <c>{0}</c> guardrail id, <c>{1}</c> <see cref="GuardrailStage"/>.</summary>
    public const string NodeBlockedByGuardrail = "NodeBlockedByGuardrail";

    /// <summary>One tool call was refused before it ran. <c>{0}</c> guardrail id, <c>{1}</c> tool name.</summary>
    public const string ToolCallBlockedByGuardrail = "ToolCallBlockedByGuardrail";

    /// <summary>The refusal as <see cref="GuardedToolHandler"/> reports it. <c>{0}</c> guardrail id, <c>{1}</c> reason.</summary>
    public const string ToolCallRefusedByGuardrail = "ToolCallRefusedByGuardrail";

    /// <summary>The flow failed validation, so nothing ran. <c>{0}</c> the joined issue messages.</summary>
    public const string FlowNotValidated = "FlowNotValidated";

    /// <summary>The step budget ran out. <c>{0}</c> the budget, <c>{1}</c> the node that would have run next.</summary>
    public const string StepBudgetExhausted = "StepBudgetExhausted";

    /// <summary>The step budget ran out, stated as a failure. <c>{0}</c> the budget.</summary>
    public const string StepBudgetReached = "StepBudgetReached";

    /// <summary>An agent node's agent is not on this host. <c>{0}</c> agent id.</summary>
    public const string AgentUnavailable = "AgentUnavailable";

    /// <summary>An agent node's agent could not be resolved, stated as a failure. <c>{0}</c> agent id.</summary>
    public const string AgentUnresolvable = "AgentUnresolvable";

    /// <summary>An agent node threw. <c>{0}</c> the exception message.</summary>
    public const string AgentStepFailed = "AgentStepFailed";

    /// <summary>A tool node cannot run because the runtime has no tool handler. <c>{0}</c> tool name.</summary>
    public const string ToolHandlerMissing = "ToolHandlerMissing";

    /// <summary>The runtime has no tool handler, stated as a failure. No arguments.</summary>
    public const string NoToolHandlerConfigured = "NoToolHandlerConfigured";

    /// <summary>An edge with no label was followed. <c>{0}</c> source node name, <c>{1}</c> destination node name.</summary>
    public const string RouteToNode = "RouteToNode";

    /// <summary>A handoff carried no variables. <c>{0}</c> context mode, <c>{1}</c> payload length.</summary>
    public const string HandoffNoVariables = "HandoffNoVariables";

    /// <summary>A handoff carried variables. <c>{0}</c> context mode, <c>{1}</c> payload length, <c>{2}</c> variable names.</summary>
    public const string HandoffCarryingVariables = "HandoffCarryingVariables";
}
