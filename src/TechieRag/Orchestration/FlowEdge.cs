namespace TechieRag.Orchestration;

/// <summary>
/// A directed, optionally conditional transition between two nodes (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>Selection rule.</b> When a node finishes, its outgoing edges are considered in
/// ascending <see cref="Order"/> (ties broken by declaration order) and the FIRST whose
/// <see cref="Condition"/> is satisfied is taken. An edge with a null condition is unconditional and
/// therefore acts as the default — which is why it should be given the highest
/// <see cref="Order"/>, and why <see cref="FlowValidator"/> warns when an unconditional edge sits
/// ahead of a conditional one and makes it unreachable.</para>
/// <para><b>No edge taken ends the run.</b> A node whose conditions are all unsatisfied has nowhere
/// to go; the run completes with whatever output it had rather than throwing, and the trace records
/// the dead end. <see cref="FlowValidator"/> flags a branch set with no default so the author sees
/// it at edit time.</para>
/// </remarks>
public sealed class FlowEdge
{
    /// <summary>Gets or sets the edge identifier, unique within the flow.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the id of the node this edge leaves.</summary>
    public required string FromNodeId { get; set; }

    /// <summary>Gets or sets the id of the node this edge enters.</summary>
    public required string ToNodeId { get; set; }

    /// <summary>Gets or sets the label shown on the canvas and in the routing trace entry.</summary>
    public string? Label { get; set; }

    /// <summary>Gets or sets the evaluation priority; lower is considered first. Defaults to 0.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the predicate guarding this edge. Null makes the edge unconditional — the
    /// default branch.
    /// </summary>
    public FlowCondition? Condition { get; set; }

    /// <summary>Gets whether this edge is always taken when reached.</summary>
    public bool IsDefault => Condition is null || Condition.Kind == FlowConditionKind.Always;
}
