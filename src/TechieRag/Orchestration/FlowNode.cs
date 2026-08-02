namespace TechieRag.Orchestration;

/// <summary>
/// One node of a <see cref="FlowDefinition"/> — a single unit of work, routing point or endpoint
/// in a multi-agent orchestration (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>One shape, kind-dependent fields.</b> Every node is this type; <see cref="Kind"/> says
/// which of the optional properties apply. <see cref="FlowNodeCatalog"/> publishes that mapping so a
/// builder UI can render the right editor per kind without hard-coding it, and
/// <see cref="FlowValidator"/> enforces it so a flow that names a tool on an agent node is rejected
/// before it runs rather than silently ignored.</para>
/// <para><b>Mutable, on purpose.</b> A flow-builder UI edits nodes in place and serializes the
/// result; immutable records would force a rebuild of the whole graph on every keystroke.</para>
/// </remarks>
public sealed class FlowNode
{
    /// <summary>Gets or sets the node identifier, unique within the flow. Referenced by every edge.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets what this node does.</summary>
    public required FlowNodeKind Kind { get; set; }

    /// <summary>Gets or sets the human-readable label shown on the canvas and in the trace.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional longer description of the node's purpose.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the agent this node runs, resolved through <see cref="IFlowAgentResolver"/>.
    /// Required for <see cref="FlowNodeKind.Agent"/>; ignored otherwise.
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Gets or sets an instruction prefixed to the node's input before the agent sees it — the
    /// per-node task, as distinct from the agent's own standing system prompt. Null passes the
    /// incoming text through unchanged.
    /// </summary>
    public string? Instruction { get; set; }

    /// <summary>
    /// Gets or sets the tool this node calls directly. Required for <see cref="FlowNodeKind.Tool"/>;
    /// ignored otherwise.
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// Gets or sets the JSON arguments passed to <see cref="ToolName"/>, supporting the
    /// <c>{{input}}</c> and <c>{{var:name}}</c> placeholders. Null sends
    /// <c>{"input":"&lt;the node's input&gt;"}</c>.
    /// </summary>
    public string? ToolArgumentsJson { get; set; }

    /// <summary>
    /// Gets or sets the guardrails this node's author attached, resolved through
    /// <see cref="IFlowGuardrailResolver"/>. These are the FLOW AUTHOR's checks; the host's own
    /// guardrails come from <see cref="FlowRuntime.HostGuardrails"/> and cannot be removed by
    /// editing a flow.
    /// </summary>
    public List<string> GuardrailIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the flow variable this node's output is written to, making it readable by later
    /// conditions and by handoffs that name it. Null discards everything but the running output.
    /// </summary>
    public string? OutputVariable { get; set; }

    /// <summary>
    /// Gets or sets the transfer this node performs. Required for
    /// <see cref="FlowNodeKind.Handoff"/>; ignored otherwise.
    /// </summary>
    public FlowHandoff? Handoff { get; set; }

    /// <summary>
    /// Gets or sets the per-node ceiling on tool-calling iterations for an agent node, overriding
    /// the agent's own default. Null uses <see cref="FlowAgent.MaxToolCalls"/>.
    /// </summary>
    public int? MaxToolCalls { get; set; }

    /// <summary>
    /// Gets or sets the outcome label a <see cref="FlowNodeKind.Terminal"/> node reports, so a flow
    /// with several endpoints can say WHICH one it reached. Null reports the node's name.
    /// </summary>
    public string? TerminalStatus { get; set; }

    /// <summary>
    /// Gets or sets presentation data the library never interprets — canvas coordinates, colours,
    /// collapsed state. It round-trips through <see cref="FlowSerializer"/> so a builder UI does not
    /// need a second, parallel store for layout.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>Gets the label used in traces and validation messages.</summary>
    /// <returns>The node's <see cref="Name"/> when set, otherwise its <see cref="Id"/>.</returns>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}
