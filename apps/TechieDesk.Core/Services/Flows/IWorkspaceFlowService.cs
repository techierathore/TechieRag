using TechieDesk.Services.Agents;
using TechieRag.Models;
using TechieRag.Orchestration;

namespace TechieDesk.Services.Flows;

/// <summary>
/// What the flow builder screen asks for: a workspace's stored flows, the palette inputs it renders,
/// and a way to run one (REQ-UI-040 / BRD-92).
/// </summary>
/// <remarks>
/// <para><b>The library owns the model; this owns the application of it.</b> Nothing here
/// re-implements a node, an edge, a condition, the validator or the runner —
/// <see cref="FlowNodeCatalog"/>, <see cref="FlowValidator"/> and <see cref="FlowRunner"/> are used
/// directly by the screen. This interface is persistence, workspace scoping, the two resolvers and
/// the host guardrails: precisely the five things REQ-RAG-042 left to the app.</para>
/// <para><b>Reading contacts nothing.</b> Listing flows, agents or tool names touches the database
/// and the in-process catalogues only; no MCP server is started and no request leaves the machine
/// until a flow is actually RUN (REQ-NFR-008).</para>
/// </remarks>
public interface IWorkspaceFlowService
{
    /// <summary>Gets the guardrails a node editor may offer, in palette order.</summary>
    IReadOnlyList<IFlowGuardrail> Guardrails { get; }

    /// <summary>
    /// Lists a workspace's flows, tolerating a document that cannot be read.
    /// </summary>
    /// <param name="workspaceId">The owning workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>One item per stored row; unreadable rows carry their error rather than being dropped.</returns>
    /// <remarks>
    /// A row whose <c>DefinitionJson</c> was hand-edited, truncated or written by a newer schema
    /// comes back with a null definition and a reason. It is neither hidden nor allowed to take the
    /// page down: a user with ten flows and one bad row must still see the other nine.
    /// </remarks>
    Task<IReadOnlyList<FlowListItem>> ListAsync(
        string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Reads one flow.</summary>
    /// <param name="workspaceId">The owning workspace identifier.</param>
    /// <param name="flowId">The flow identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The item, or null when this workspace has no such flow.</returns>
    Task<FlowListItem?> FindAsync(
        string workspaceId, string flowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a composed flow, serialized by the library and stored verbatim.
    /// </summary>
    /// <param name="workspaceId">The owning workspace identifier.</param>
    /// <param name="flow">The flow to store.</param>
    /// <param name="isEnabled">Whether the flow may be run.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the flow is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="flow"/> is null.</exception>
    Task SaveAsync(
        string workspaceId, FlowDefinition flow, bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>Removes one flow.</summary>
    /// <param name="workspaceId">The owning workspace identifier.</param>
    /// <param name="flowId">The flow identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><see langword="true"/> when a flow was removed.</returns>
    Task<bool> DeleteAsync(
        string workspaceId, string flowId, CancellationToken cancellationToken = default);

    /// <summary>Suspends or resumes a flow.</summary>
    /// <param name="workspaceId">The owning workspace identifier.</param>
    /// <param name="flowId">The flow identifier.</param>
    /// <param name="isEnabled">True to allow the flow to be run.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the flow was found and updated.</returns>
    Task<bool> SetEnabledAsync(
        string workspaceId, string flowId, bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>Lists the agents the node editor's agent picker may offer.</summary>
    /// <param name="workspaceId">The owning workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The workspace's agents, including the built-in one.</returns>
    Task<IReadOnlyList<AgentDefinition>> ListAgentsAsync(
        string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the tool names a <see cref="FlowNodeKind.Tool"/> node may call.
    /// </summary>
    /// <param name="workspaceId">The owning workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The implemented catalogue skills, plus any tool a registered MCP server was last seen advertising.</returns>
    /// <remarks>
    /// The MCP half comes from the CACHED advertised-tool list on each registration, never from
    /// dialling the servers: painting a picker must not be an outbound request. A server registered
    /// but never tested contributes nothing here, which is honest — nothing has ever seen its tools.
    /// </remarks>
    Task<IReadOnlyList<string>> ListToolNamesAsync(
        string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a flow on this workspace's agents, tools and guardrails.
    /// </summary>
    /// <param name="workspaceId">The workspace whose capabilities the flow may use.</param>
    /// <param name="flow">The flow to run.</param>
    /// <param name="input">The text the run starts from.</param>
    /// <param name="confirmation">
    /// How to ask the user before anything leaves the machine, or null when this caller cannot ask —
    /// in which case egress is denied rather than assumed (REQ-NFR-013).
    /// </param>
    /// <param name="progress">Live sink for trace steps, or null to collect them only in the result.</param>
    /// <param name="cancellationToken">Token to cancel the run.</param>
    /// <returns>What the flow produced, how it got there, and why it stopped.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="flow"/> or <paramref name="input"/> is null.</exception>
    /// <remarks>
    /// Never throws for a flow-level problem: an invalid flow, an unresolvable agent, a guardrail
    /// refusal and an exhausted budget are all outcomes on <see cref="FlowRunResult"/>, because a
    /// screen showing a run needs to say what happened.
    /// </remarks>
    Task<FlowRunResult> RunAsync(
        string workspaceId,
        FlowDefinition flow,
        string input,
        IEgressConfirmation? confirmation,
        IProgress<AgentStep>? progress,
        CancellationToken cancellationToken = default);
}
