namespace TechieDesk.Services.Flows;

/// <summary>
/// Durable, workspace-scoped storage for composed agent flows (REQ-UI-040 / BRD-92).
/// </summary>
/// <remarks>
/// <para><b>Every method takes the workspace id, and it is never optional.</b> A flow is a capability
/// composition — it names agents, tools and guardrails — so the finance workspace's flow must not be
/// listable, editable, runnable or deletable from the marketing workspace merely because both live in
/// one install. The id is part of the WHERE clause of every statement rather than a filter applied to
/// a wider result, so there is no query on this interface that could return another workspace's row.</para>
/// </remarks>
public interface IFlowRepository
{
    /// <summary>Lists a workspace's flows, newest edit first.</summary>
    /// <param name="workspaceId">The owning workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The stored rows; empty when the workspace has none.</returns>
    Task<IReadOnlyList<FlowRecord>> ListAsync(
        string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Reads one flow.</summary>
    /// <param name="workspaceId">The owning workspace identifier.</param>
    /// <param name="flowId">The flow identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The row, or null when this workspace has no such flow.</returns>
    Task<FlowRecord?> FindAsync(
        string workspaceId, string flowId, CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces one flow.</summary>
    /// <param name="record">The row to store.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task SaveAsync(FlowRecord record, CancellationToken cancellationToken = default);

    /// <summary>Removes one flow.</summary>
    /// <param name="workspaceId">The owning workspace identifier.</param>
    /// <param name="flowId">The flow identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><see langword="true"/> when a row was removed.</returns>
    Task<bool> DeleteAsync(
        string workspaceId, string flowId, CancellationToken cancellationToken = default);

    /// <summary>Suspends or resumes a flow without losing its definition.</summary>
    /// <param name="workspaceId">The owning workspace identifier.</param>
    /// <param name="flowId">The flow identifier.</param>
    /// <param name="isEnabled">True to allow the flow to be run.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the row was found and updated.</returns>
    Task<bool> SetEnabledAsync(
        string workspaceId, string flowId, bool isEnabled, CancellationToken cancellationToken = default);
}
