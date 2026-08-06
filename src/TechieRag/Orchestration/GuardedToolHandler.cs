using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Orchestration;

/// <summary>
/// Wraps an agent's tools so a flow's guardrails see every call before it is dispatched
/// (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>An additional choke point, not a replacement.</b> The wrapped handler is whatever the
/// host supplied on <see cref="FlowAgent.Tools"/>, including any gating the host had already applied
/// to it — TechieDesk wraps its egress-marked skills before the handler ever reaches the library, and
/// that wrapping still runs here. This adds one place where a host can see and refuse every call a
/// flow makes, at the point the call would be dispatched.</para>
/// <para><b>A refusal is a tool result, never an exception.</b> The same contract
/// <c>ToolRegistry</c> and <c>McpToolHandler</c> already keep: a blocked call comes back as an
/// unsuccessful <see cref="ToolResult"/> the model can read and work around, so the agent can report
/// the tool as unavailable and finish the turn. Throwing would end the turn and lose the answer.</para>
/// <para><b>Never silent.</b> Every refusal is reported through <see cref="OnBlocked"/>, which the
/// runner turns into a <see cref="Models.AgentStepKind.GuardrailBlocked"/> trace entry. A blocked
/// call that only showed up as a slightly odd model reply would be indistinguishable from the model
/// choosing not to call the tool.</para>
/// <para><b>The tool list is unchanged.</b> Guarded tools stay visible to the model, exactly as the
/// app's <c>EgressGate</c> does it: hiding them would change what the model believes it can do and
/// stop it from telling the user why something did not happen.</para>
/// <para><b>A refusal has two readers, and they get different things (REQ-RAG-050).</b>
/// <see cref="ToolResult.Content"/> is read by the MODEL, so it stays a finished English sentence it
/// can act on. <see cref="ToolResult.ErrorMessage"/> is what a host paints as the detail line of a
/// trace row, so the same refusal is also published as a <see cref="FlowMessage"/> — a stable code
/// plus the guardrail id and the reason — through <see cref="RefusalMessage"/>, and stamped onto
/// <see cref="FlowStep.FailureMessage"/> by <see cref="FlowRunner"/>. Without it a Hindi user meets
/// an English refusal at the exact moment something went wrong, which is the defect BRD-91 names.</para>
/// </remarks>
public sealed class GuardedToolHandler : IToolHandler
{
    /// <summary>The stand-in reason for a refusal that named none.</summary>
    private static readonly FlowMessage NoReasonGiven = FlowMessage.Create(
        FlowMessageCodes.GuardrailRefusedCall, "The call was refused by a guardrail.");

    private readonly IToolHandler inner;
    private readonly FlowGuardrailChain chain;
    private readonly FlowNode node;
    private readonly IReadOnlyDictionary<string, string> variables;
    private readonly Action<GuardrailVerdict, ToolCall>? onBlocked;

    /// <summary>
    /// Wraps a handler with a node's guardrail chain.
    /// </summary>
    /// <param name="inner">The handler whose calls are guarded.</param>
    /// <param name="chain">The node's guardrails, host checks first.</param>
    /// <param name="node">The node the calls happen in, named in the guardrail context.</param>
    /// <param name="variables">The run's flow variables, exposed read-only to the guardrails.</param>
    /// <param name="onBlocked">Raised for each refusal so the run's trace can record it.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public GuardedToolHandler(
        IToolHandler inner,
        FlowGuardrailChain chain,
        FlowNode node,
        IReadOnlyDictionary<string, string>? variables = null,
        Action<GuardrailVerdict, ToolCall>? onBlocked = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(node);

        this.inner = inner;
        this.chain = chain;
        this.node = node;
        this.variables = variables ?? new Dictionary<string, string>();
        this.onBlocked = onBlocked;
    }

    /// <inheritdoc/>
    /// <remarks>The wrapped handler's list, unmodified: guarding changes what runs, not what is offered.</remarks>
    public IReadOnlyList<ToolDefinition> ToolDefinitions => inner.ToolDefinitions;

    /// <summary>Gets the callback raised whenever a call is refused.</summary>
    public Action<GuardrailVerdict, ToolCall>? OnBlocked => onBlocked;

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        var definition = inner.ToolDefinitions
            .FirstOrDefault(item => string.Equals(item.Name, toolCall.Name, StringComparison.OrdinalIgnoreCase));

        var verdict = await chain.EvaluateAsync(
            new GuardrailContext(
                GuardrailStage.ToolCall,
                node.Id,
                node.DisplayName,
                toolCall.ArgumentsJson,
                toolCall.Name,
                definition?.Description,
                node.AgentId,
                variables),
            cancellationToken).ConfigureAwait(false);

        if (verdict.IsAllowed)
        {
            return await inner.ExecuteToolAsync(toolCall, cancellationToken).ConfigureAwait(false);
        }

        onBlocked?.Invoke(verdict, toolCall);

        var refusal = RefusalMessage(verdict);
        return new ToolResult
        {
            ToolCallId = toolCall.Id,
            Content = $"unavailable: '{toolCall.Name}' was not run. {ReasonText(verdict)}",
            IsSuccess = false,
            ErrorMessage = refusal.Text,
            // Publish the code alongside the English so it survives the trip through
            // AgentLoopRunner's ToolExecuted step and out to a renderer (REQ-RAG-051).
            Message = refusal
        };
    }

    /// <summary>
    /// Builds the localizable form of a refusal — the same sentence this handler puts on
    /// <see cref="ToolResult.ErrorMessage"/>, as a code plus its arguments (REQ-RAG-050).
    /// </summary>
    /// <param name="verdict">The refusal, normally the one handed to <see cref="OnBlocked"/>.</param>
    /// <returns>A <see cref="FlowMessage"/> under <see cref="FlowMessageCodes.ToolCallRefusedByGuardrail"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="verdict"/> is null.</exception>
    /// <remarks>
    /// <para>Public and static because a host that wraps this handler, or renders a trace rebuilt
    /// from a persisted run, needs to reconstruct the identical sentence without re-running the
    /// guardrail.</para>
    /// <para><b>Argument <c>{1}</c> is a nested sentence, and that is deliberate.</b> A refusal is
    /// always "this framing, around that guardrail's reason". The framing is the library's and has
    /// this code; the reason is the GUARDRAIL's and carries its own code on
    /// <see cref="GuardrailVerdict.Message"/>. A consumer should translate the framing, translate
    /// <see cref="GuardrailVerdict.Message"/> separately, and substitute the second into the first.
    /// The English here is the fallback for a consumer that does neither — and for a host guardrail
    /// that returned a plain <see cref="GuardrailDecision.Block(string)"/> string with no code at
    /// all, it is the only thing available, which is the host's own choice to make.</para>
    /// </remarks>
    public static FlowMessage RefusalMessage(GuardrailVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return FlowMessage.Create(
            FlowMessageCodes.ToolCallRefusedByGuardrail,
            "Blocked by guardrail '{0}': {1}",
            verdict.GuardrailId ?? string.Empty,
            ReasonText(verdict));
    }

    /// <summary>Resolves the English reason to quote, falling back when the refusal carried none.</summary>
    /// <param name="verdict">The refusal.</param>
    /// <returns>The reason text; never null.</returns>
    private static string ReasonText(GuardrailVerdict verdict) =>
        verdict.Reason ?? verdict.Message?.Text ?? NoReasonGiven.Text;
}
