namespace TechieDesk.Services.Agents;

/// <summary>
/// Persistence for named, user-defined agents (REQ-UI-045). Dapper over the app database; EF Core
/// is banned by BRD-102.
/// </summary>
public interface IAgentRepository
{
    /// <summary>Lists every stored agent in a workspace, with its skill selection loaded.</summary>
    /// <param name="workspaceId">The owning workspace.</param>
    /// <returns>The stored agents, built-in first then alphabetically by handle.</returns>
    Task<IReadOnlyList<AgentDefinition>> ListAsync(string workspaceId);

    /// <summary>Finds one stored agent by its chat handle.</summary>
    /// <param name="workspaceId">The owning workspace.</param>
    /// <param name="handle">The normalized handle, without its leading '@'.</param>
    /// <returns>The agent, or null when the workspace has no agent with that handle.</returns>
    Task<AgentDefinition?> FindByHandleAsync(string workspaceId, string handle);

    /// <summary>
    /// Inserts or updates an agent and replaces its skill selection.
    /// </summary>
    /// <param name="agent">The agent to persist; <c>WorkspaceAgentId</c> 0 inserts.</param>
    /// <returns>The stored identifier.</returns>
    Task<long> SaveAsync(AgentDefinition agent);

    /// <summary>Deletes an agent and its skill rows.</summary>
    /// <param name="workspaceAgentId">The agent identifier.</param>
    Task DeleteAsync(long workspaceAgentId);

    /// <summary>Records that an agent answered a turn, for the "last used" column.</summary>
    /// <param name="workspaceAgentId">The agent identifier.</param>
    /// <param name="usedAt">When the agent ran (UTC).</param>
    Task TouchAsync(long workspaceAgentId, DateTime usedAt);
}
