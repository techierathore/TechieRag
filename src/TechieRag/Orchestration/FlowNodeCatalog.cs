namespace TechieRag.Orchestration;

/// <summary>
/// One editable property of a node kind, as a builder UI needs to render it (REQ-RAG-042).
/// </summary>
/// <param name="Property">The <see cref="FlowNode"/> property name, matching the serialized field.</param>
/// <param name="DisplayName">The label to show for it.</param>
/// <param name="Description">One line explaining what it does.</param>
/// <param name="IsRequired">Whether <see cref="FlowValidator"/> reports an error when it is unset.</param>
/// <param name="Editor">A hint at the control to use — see <see cref="FlowFieldEditors"/>.</param>
public sealed record FlowFieldDescriptor(
    string Property,
    string DisplayName,
    string Description,
    bool IsRequired,
    string Editor);

/// <summary>The editor hints <see cref="FlowFieldDescriptor.Editor"/> uses (REQ-RAG-042).</summary>
/// <remarks>
/// Strings rather than an enum on purpose: this is a rendering HINT for a UI the library does not
/// own, and a host with a better control for a field should not need a library release to use it.
/// </remarks>
public static class FlowFieldEditors
{
    /// <summary>A single-line text box.</summary>
    public const string Text = "text";

    /// <summary>A multi-line text area, for prompts and instructions.</summary>
    public const string MultilineText = "multiline";

    /// <summary>A picker over <see cref="IFlowAgentResolver.ListAgentsAsync"/>.</summary>
    public const string AgentPicker = "agent";

    /// <summary>A picker over the runtime's <see cref="FlowRuntime.Tools"/> definitions.</summary>
    public const string ToolPicker = "tool";

    /// <summary>A multi-select over <see cref="IFlowGuardrailResolver.ListGuardrailsAsync"/>.</summary>
    public const string GuardrailPicker = "guardrails";

    /// <summary>A picker over the flow's own nodes.</summary>
    public const string NodePicker = "node";

    /// <summary>A JSON editor.</summary>
    public const string Json = "json";

    /// <summary>A whole number.</summary>
    public const string Number = "number";

    /// <summary>The handoff sub-editor: context mode plus the carried-variable allowlist.</summary>
    public const string Handoff = "handoff";
}

/// <summary>
/// Everything a builder UI needs to render one node kind (REQ-RAG-042).
/// </summary>
/// <param name="Kind">The kind being described.</param>
/// <param name="DisplayName">The palette label.</param>
/// <param name="Description">What dropping this node on the canvas does.</param>
/// <param name="Fields">The properties that apply to this kind, in editor order.</param>
/// <param name="AllowsOutgoingEdges">False for a terminal node, which ends the run.</param>
/// <param name="AllowsConditionalEdges">Whether outgoing edges may carry conditions.</param>
/// <param name="UsesLlm">Whether executing this node calls a model. False means it costs no tokens.</param>
public sealed record FlowNodeKindDescriptor(
    FlowNodeKind Kind,
    string DisplayName,
    string Description,
    IReadOnlyList<FlowFieldDescriptor> Fields,
    bool AllowsOutgoingEdges,
    bool AllowsConditionalEdges,
    bool UsesLlm);

/// <summary>
/// The palette: what a flow builder may place on a canvas, and how to edit it (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>Why the library publishes this.</b> The alternative is a UI with a hard-coded list of
/// node kinds and a hard-coded list of which fields each one uses. That list is a copy of a fact the
/// library already knows, and the day a kind is added it is a copy that is wrong — the palette is
/// missing an entry and nothing fails. Enumerating <see cref="Kinds"/> makes the palette derive
/// from the model.</para>
/// <para><b>It is the same fact <see cref="FlowValidator"/> enforces.</b> A field marked
/// <see cref="FlowFieldDescriptor.IsRequired"/> here is a field whose absence the validator reports
/// as an error, so the editor and the validator cannot disagree about what a valid node is.</para>
/// </remarks>
public static class FlowNodeCatalog
{
    private static readonly FlowFieldDescriptor NameField = new(
        nameof(FlowNode.Name), "Name", "The label shown on the canvas and in the run trace.",
        false, FlowFieldEditors.Text);

    private static readonly FlowFieldDescriptor DescriptionField = new(
        nameof(FlowNode.Description), "Description", "An optional note about this node's purpose.",
        false, FlowFieldEditors.MultilineText);

    private static readonly FlowFieldDescriptor GuardrailsField = new(
        nameof(FlowNode.GuardrailIds), "Guardrails",
        "Checks that may refuse this node's input, its output, or a tool it calls. The host's own guardrails always apply in addition to these.",
        false, FlowFieldEditors.GuardrailPicker);

    private static readonly FlowFieldDescriptor OutputVariableField = new(
        nameof(FlowNode.OutputVariable), "Output variable",
        "Stores this node's output under a name that later conditions and handoffs can read.",
        false, FlowFieldEditors.Text);

    /// <summary>Gets every node kind a flow may contain, in the order a palette should list them.</summary>
    public static IReadOnlyList<FlowNodeKindDescriptor> Kinds { get; } =
    [
        new(
            FlowNodeKind.Agent,
            "Agent",
            "Runs one agent turn: the agent's model, its system prompt and its tools, looping until it answers.",
            [
                NameField,
                DescriptionField,
                new(nameof(FlowNode.AgentId), "Agent", "Which agent answers this step.", true, FlowFieldEditors.AgentPicker),
                new(nameof(FlowNode.Instruction), "Instruction", "The task for this step, prefixed to the incoming text. The agent's own system prompt is separate.", false, FlowFieldEditors.MultilineText),
                new(nameof(FlowNode.MaxToolCalls), "Max tool calls", "Ceiling on tool-calling iterations for this step; blank uses the agent's own.", false, FlowFieldEditors.Number),
                GuardrailsField,
                OutputVariableField
            ],
            AllowsOutgoingEdges: true,
            AllowsConditionalEdges: true,
            UsesLlm: true),

        new(
            FlowNodeKind.Tool,
            "Tool",
            "Calls one tool directly, with no model in the path. Deterministic glue between agents.",
            [
                NameField,
                DescriptionField,
                new(nameof(FlowNode.ToolName), "Tool", "Which tool to call.", true, FlowFieldEditors.ToolPicker),
                new(nameof(FlowNode.ToolArgumentsJson), "Arguments", "JSON arguments, supporting {{input}} and {{var:name}}. Blank sends the incoming text as \"input\".", false, FlowFieldEditors.Json),
                GuardrailsField,
                OutputVariableField
            ],
            AllowsOutgoingEdges: true,
            AllowsConditionalEdges: true,
            UsesLlm: false),

        new(
            FlowNodeKind.Condition,
            "Condition",
            "A branch point. Evaluates its outgoing edges and routes; costs no tokens and makes no request.",
            [NameField, DescriptionField],
            AllowsOutgoingEdges: true,
            AllowsConditionalEdges: true,
            UsesLlm: false),

        new(
            FlowNodeKind.Handoff,
            "Handoff",
            "Transfers control to another agent, carrying exactly the context the handoff declares and nothing else.",
            [
                NameField,
                DescriptionField,
                new(nameof(FlowNode.Handoff), "Handoff", "The receiving agent node, how much context crosses, and which variables it may see.", true, FlowFieldEditors.Handoff),
                GuardrailsField
            ],
            AllowsOutgoingEdges: false,
            AllowsConditionalEdges: false,
            UsesLlm: false),

        new(
            FlowNodeKind.Terminal,
            "End",
            "Ends the run and produces the flow's output.",
            [
                NameField,
                DescriptionField,
                new(nameof(FlowNode.TerminalStatus), "Outcome label", "Names WHICH ending this is, for a flow with several.", false, FlowFieldEditors.Text),
                new(nameof(FlowNode.Instruction), "Output override", "Text to return instead of the incoming output; blank returns what arrived.", false, FlowFieldEditors.MultilineText)
            ],
            AllowsOutgoingEdges: false,
            AllowsConditionalEdges: false,
            UsesLlm: false)
    ];

    /// <summary>Gets the condition operators an edge editor may offer.</summary>
    /// <remarks>
    /// Derived from the enum rather than listed, so an operator added to
    /// <see cref="FlowConditionKind"/> appears in the editor without a second edit.
    /// </remarks>
    public static IReadOnlyList<FlowConditionKind> ConditionKinds { get; } =
        Enum.GetValues<FlowConditionKind>();

    /// <summary>Gets the values a condition may read.</summary>
    public static IReadOnlyList<FlowConditionSource> ConditionSources { get; } =
        Enum.GetValues<FlowConditionSource>();

    /// <summary>Gets the handoff context modes, narrowest first.</summary>
    public static IReadOnlyList<HandoffContextMode> HandoffContextModes { get; } =
        Enum.GetValues<HandoffContextMode>();

    /// <summary>Describes one node kind.</summary>
    /// <param name="kind">The kind to describe.</param>
    /// <returns>Its descriptor.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the kind has no descriptor, which means the catalogue was not updated with the enum.</exception>
    public static FlowNodeKindDescriptor Describe(FlowNodeKind kind) =>
        Kinds.FirstOrDefault(descriptor => descriptor.Kind == kind)
        ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "No catalogue entry describes this node kind.");

    /// <summary>
    /// Creates a node of the given kind with a fresh id, ready to be placed on a canvas.
    /// </summary>
    /// <param name="kind">The kind to create.</param>
    /// <param name="id">The id to give it; null generates one.</param>
    /// <returns>A new node with its kind's display name pre-filled.</returns>
    public static FlowNode CreateNode(FlowNodeKind kind, string? id = null) => new()
    {
        Id = string.IsNullOrWhiteSpace(id) ? $"{kind.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..24] : id,
        Kind = kind,
        Name = Describe(kind).DisplayName
    };
}
