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
/// </remarks>
public sealed class GuardedToolHandler : IToolHandler
{
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

        var reason = verdict.Reason ?? "The call was refused by a guardrail.";
        return new ToolResult
        {
            ToolCallId = toolCall.Id,
            Content = $"unavailable: '{toolCall.Name}' was not run. {reason}",
            IsSuccess = false,
            ErrorMessage = $"Blocked by guardrail '{verdict.GuardrailId}': {reason}"
        };
    }
}
