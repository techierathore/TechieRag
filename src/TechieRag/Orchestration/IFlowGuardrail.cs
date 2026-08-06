namespace TechieRag.Orchestration;

/// <summary>Where in a node's lifecycle a guardrail is consulted (REQ-RAG-042).</summary>
public enum GuardrailStage
{
    /// <summary>Before the node runs, inspecting what is about to be given to it.</summary>
    Input,

    /// <summary>After the node runs, inspecting what it produced.</summary>
    Output,

    /// <summary>
    /// Before an individual tool call is dispatched, inspecting the tool name and its arguments.
    /// This is the stage a host's egress gate plugs into.
    /// </summary>
    ToolCall
}

/// <summary>
/// What a guardrail is being asked to judge (REQ-RAG-042).
/// </summary>
/// <param name="Stage">Where in the node lifecycle the check is happening.</param>
/// <param name="NodeId">The node being guarded.</param>
/// <param name="NodeName">The node's display name, for the trace and for any prompt shown to a user.</param>
/// <param name="Payload">The text under inspection: the node input, the node output, or the tool arguments JSON.</param>
/// <param name="ToolName">The tool about to run, for <see cref="GuardrailStage.ToolCall"/>; null otherwise.</param>
/// <param name="ToolDescription">The tool's description as the model saw it, for <see cref="GuardrailStage.ToolCall"/>; null otherwise.</param>
/// <param name="AgentId">The agent the node runs, when it has one; null otherwise.</param>
/// <param name="Variables">The run's flow variables, read-only.</param>
/// <remarks>
/// <para><b>The tool fields are why per-tool provenance matters.</b> A host deciding whether a call
/// leaves the machine needs to know WHICH tool, not just that a tool ran. For MCP-backed tools the
/// server behind a qualified name is available from <c>McpWorkspaceTools.ToolsByServer</c> and
/// <c>McpToolHandler.ServerNameFor</c> (TR-RAG-041), so the decision is a lookup rather than a
/// guess made from a string prefix.</para>
/// </remarks>
public sealed record GuardrailContext(
    GuardrailStage Stage,
    string NodeId,
    string NodeName,
    string Payload,
    string? ToolName = null,
    string? ToolDescription = null,
    string? AgentId = null,
    IReadOnlyDictionary<string, string>? Variables = null);

/// <summary>
/// A guardrail's verdict (REQ-RAG-042).
/// </summary>
/// <remarks>
/// Allow/block only, deliberately. A "rewrite the payload" verdict was considered and rejected: a
/// guardrail that can silently alter what an agent sees makes a trace stop being a true account of
/// the run, and the same effect is available honestly as a <see cref="FlowNodeKind.Tool"/> node.
/// </remarks>
public sealed class GuardrailDecision
{
    /// <summary>A verdict that lets the step proceed.</summary>
    public static readonly GuardrailDecision Allowed = new(true, null);

    private GuardrailDecision(bool isAllowed, string? reason, FlowMessage? message = null)
    {
        IsAllowed = isAllowed;
        Reason = reason;
        Message = message;
    }

    /// <summary>Gets whether the guarded step may proceed.</summary>
    public bool IsAllowed { get; }

    /// <summary>
    /// Gets why the step was refused. Surfaced in the trace and handed to the model for a blocked
    /// tool call, so a refusal is never a silent no-op.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Gets the localizable form of <see cref="Reason"/> — a stable code plus its arguments — or
    /// null when the guardrail supplied only English (REQ-RAG-050).
    /// </summary>
    /// <remarks>
    /// The reason a person reads on a screen. A guardrail that wants its refusal to be readable in
    /// a language this library does not ship should block with
    /// <see cref="Block(FlowMessage)"/>; <see cref="Block(string)"/> stays for the many hosts whose
    /// refusal text is already theirs to translate.
    /// </remarks>
    public FlowMessage? Message { get; }

    /// <summary>Creates an allowing verdict.</summary>
    /// <returns>The shared allowing verdict.</returns>
    public static GuardrailDecision Allow() => Allowed;

    /// <summary>Creates a blocking verdict.</summary>
    /// <param name="reason">Why the step was refused; shown in the trace and to the model.</param>
    /// <returns>A blocking verdict carrying the reason.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is blank.</exception>
    public static GuardrailDecision Block(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new GuardrailDecision(false, reason);
    }

    /// <summary>
    /// Creates a blocking verdict whose reason a consumer can translate (REQ-RAG-050).
    /// </summary>
    /// <param name="message">The refusal, as a stable code plus its arguments.</param>
    /// <returns>A blocking verdict carrying both the code and the English wording.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
    /// <remarks>
    /// <see cref="Reason"/> is still populated, from <see cref="FlowMessage.Text"/>, so every
    /// existing reader — the model's tool result, the log, a consumer built against the old shape —
    /// is unaffected. The code is additional, never a replacement.
    /// </remarks>
    public static GuardrailDecision Block(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new GuardrailDecision(false, message.Text, message);
    }
}

/// <summary>
/// A check that can inspect and refuse a flow step (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>Deny by default, at every failure mode.</b> A guardrail that throws blocks. A guardrail
/// named by a node that the host's <see cref="IFlowGuardrailResolver"/> cannot produce blocks. A
/// node naming guardrails with no resolver configured at all blocks. The one thing that can never
/// happen is a guardrail failing to run and the step proceeding anyway — that failure mode is how a
/// gate becomes decorative.</para>
/// <para><b>The seam a host's egress gate plugs into.</b> TechieDesk's <c>EgressGate</c>
/// (REQ-NFR-013) is app code the library cannot reference. It plugs in as an
/// <see cref="IFlowGuardrail"/> placed in <see cref="FlowRuntime.HostGuardrails"/>, whose
/// <see cref="InspectAsync"/> at <see cref="GuardrailStage.ToolCall"/> calls
/// <c>EgressGate.AllowExternalAsync</c> and returns <see cref="GuardrailDecision.Block(FlowMessage)"/>
/// when the user declines. Because host guardrails are supplied at run time and are applied to EVERY node
/// and EVERY tool call, a flow cannot become a route to an egress-marked tool that skips the gate:
/// there is no property on <see cref="FlowDefinition"/> that turns them off, and removing a node's
/// own guardrail ids does not remove them.</para>
/// <para><b>Composes with the gate rather than replacing it.</b> The host's existing per-tool
/// wrapping still applies — an <c>IToolHandler</c> that was already gated stays gated when a flow
/// runs it, because the flow calls the handler the host supplied. The flow-level stage is an
/// ADDITIONAL choke point for hosts that want one place to see every call a flow makes.</para>
/// </remarks>
public interface IFlowGuardrail
{
    /// <summary>Gets the stable identifier a flow node uses to name this guardrail.</summary>
    string Id { get; }

    /// <summary>Gets a one-line description of what this guardrail refuses, shown in the trace.</summary>
    string Description { get; }

    /// <summary>Gets the stages this guardrail wants to see. Others are skipped without calling it.</summary>
    IReadOnlyList<GuardrailStage> Stages { get; }

    /// <summary>
    /// Judges one step.
    /// </summary>
    /// <param name="context">What is about to happen, or what just did.</param>
    /// <param name="cancellationToken">Token cancelled when the run is cancelled.</param>
    /// <returns>Allow to proceed, or block with a reason.</returns>
    /// <remarks>
    /// Throwing is treated as a block, not as a run failure — a broken check must not be a way to
    /// get past it. Only <see cref="OperationCanceledException"/> for the supplied token propagates,
    /// because that is the caller cancelling the whole run rather than the guardrail failing.
    /// </remarks>
    Task<GuardrailDecision> InspectAsync(GuardrailContext context, CancellationToken cancellationToken = default);
}
