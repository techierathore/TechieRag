namespace TechieRag.Orchestration;

/// <summary>
/// The kinds of node a <see cref="FlowDefinition"/> can contain (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>Why an enum and not a type hierarchy.</b> A flow is persisted by the host application
/// and edited by a no-code builder (BRD-92). A closed enum plus one <see cref="FlowNode"/> shape
/// serializes with no polymorphic converter, round-trips through any string column, and lets the
/// palette be built by enumerating <see cref="FlowNodeCatalog.Kinds"/> rather than by reflecting
/// over assemblies.</para>
/// <para><b>Adding a kind is a schema change.</b> An older reader hitting an unknown kind fails
/// loudly in <see cref="FlowSerializer"/> rather than silently dropping the node, because a flow
/// missing a node is a flow that quietly does something else.</para>
/// </remarks>
public enum FlowNodeKind
{
    /// <summary>
    /// Runs one agent turn — an LLM plus its tools — through the library's existing agent loop.
    /// </summary>
    Agent,

    /// <summary>
    /// Calls one named tool directly, with no LLM in the path. Deterministic glue between agents.
    /// </summary>
    Tool,

    /// <summary>
    /// Evaluates its outgoing edges and routes. Performs no work and has no side effect, so a
    /// branch point costs neither a token nor a request.
    /// </summary>
    Condition,

    /// <summary>
    /// Transfers control to another agent node, carrying exactly the context the handoff declares.
    /// </summary>
    Handoff,

    /// <summary>
    /// Ends the run and produces the flow's output.
    /// </summary>
    Terminal
}
