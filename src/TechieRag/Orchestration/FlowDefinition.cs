namespace TechieRag.Orchestration;

/// <summary>
/// A complete, persistable multi-agent orchestration graph (REQ-RAG-042 / BRD-123).
/// </summary>
/// <remarks>
/// <para><b>This type IS the persistence contract.</b> The host application owns storage; the
/// library owns the model and its serialization. <see cref="FlowSerializer"/> turns this into and
/// out of a single JSON string, so a host needs exactly one text column and no schema migration
/// when a node gains a property. <see cref="SchemaVersion"/> is what a reader checks before
/// trusting the rest.</para>
/// <para><b>Untrusted data.</b> A flow is authored in a builder UI and read back from storage, so
/// nothing in it is a security decision. Guardrails the flow names are the AUTHOR's business rules;
/// the host's own guardrails live in <see cref="FlowRuntime.HostGuardrails"/>, are supplied at run
/// time, and cannot be removed by editing a flow.</para>
/// <para><b>Termination.</b> Two independent mechanisms. At edit time <see cref="FlowValidator"/>
/// detects cycles and, with <see cref="AllowCycles"/> false (the default), reports them as errors so
/// the flow cannot be saved or run. At run time <see cref="MaxSteps"/> bounds the number of nodes
/// executed regardless of shape, so a flow that reached storage through some other route — an older
/// version, a hand-edited row — still stops. Neither is trusted to be the only one.</para>
/// </remarks>
public sealed class FlowDefinition
{
    /// <summary>The default ceiling on nodes executed in one run.</summary>
    public const int DefaultMaxSteps = 25;

    /// <summary>
    /// Gets or sets the serialization schema version this flow was written with.
    /// </summary>
    /// <remarks>
    /// A reader must refuse a version it does not understand rather than deserialize part of it.
    /// <see cref="FlowSerializer.FromJson"/> does exactly that.
    /// </remarks>
    public int SchemaVersion { get; set; } = FlowSerializer.CurrentSchemaVersion;

    /// <summary>Gets or sets the flow identifier, unique within the host's store.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the flow's display name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets an optional description of what the flow is for.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the node the run begins at. Null starts at the first node in
    /// <see cref="Nodes"/>, which <see cref="FlowValidator"/> reports as a warning because a graph
    /// whose entry point depends on list order is a graph one reorder away from behaving differently.
    /// </summary>
    public string? StartNodeId { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of nodes one run may execute. The run-time termination
    /// guarantee: reaching it ends the run with
    /// <see cref="FlowRunOutcome.StepBudgetExhausted"/> rather than looping.
    /// </summary>
    public int MaxSteps { get; set; } = DefaultMaxSteps;

    /// <summary>
    /// Gets or sets whether cycles are permitted. False (the default) makes a detected cycle a
    /// validation ERROR, so retry loops must be opted into deliberately; true downgrades it to a
    /// warning and leaves <see cref="MaxSteps"/> as the bound.
    /// </summary>
    public bool AllowCycles { get; set; }

    /// <summary>Gets or sets the flow's nodes.</summary>
    public List<FlowNode> Nodes { get; set; } = [];

    /// <summary>Gets or sets the flow's edges.</summary>
    public List<FlowEdge> Edges { get; set; } = [];

    /// <summary>
    /// Gets or sets presentation and host data the library never interprets — canvas zoom, author,
    /// tags. Round-trips through <see cref="FlowSerializer"/>.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>Finds a node by id.</summary>
    /// <param name="nodeId">The node identifier; null or blank returns null.</param>
    /// <returns>The node, or null when no node carries that id.</returns>
    public FlowNode? FindNode(string? nodeId) =>
        string.IsNullOrWhiteSpace(nodeId)
            ? null
            : Nodes.FirstOrDefault(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));

    /// <summary>Gets the outgoing edges of a node, in the order they are evaluated.</summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <returns>The node's outgoing edges, ascending by <see cref="FlowEdge.Order"/>, then by declaration order.</returns>
    public IReadOnlyList<FlowEdge> EdgesFrom(string nodeId) =>
        Edges
            .Select((edge, index) => (edge, index))
            .Where(pair => string.Equals(pair.edge.FromNodeId, nodeId, StringComparison.Ordinal))
            .OrderBy(pair => pair.edge.Order)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.edge)
            .ToList();

    /// <summary>Gets the node a run starts at.</summary>
    /// <returns>The declared start node, the first node when none is declared, or null when the flow is empty.</returns>
    public FlowNode? ResolveStartNode() => FindNode(StartNodeId) ?? Nodes.FirstOrDefault();
}
