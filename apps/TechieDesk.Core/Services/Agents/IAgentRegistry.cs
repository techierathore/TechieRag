namespace TechieDesk.Services.Agents;

/// <summary>
/// The workspace's agent registry: the rules on top of storage — the undeletable built-in
/// <c>@agent</c>, handle uniqueness, and the run-time skill intersection
/// (BRD-83/84/138 · REQ-UI-045 · REQ-RAG-021 · REQ-RAG-022).
/// </summary>
public interface IAgentRegistry
{
    /// <summary>
    /// Lists a workspace's agents, always including the built-in <c>@agent</c> — synthesized when
    /// it has never been edited, so a fresh workspace still has an agent to call.
    /// </summary>
    /// <param name="workspaceId">The workspace.</param>
    /// <returns>The agents, built-in first.</returns>
    Task<IReadOnlyList<AgentDefinition>> ListAsync(string workspaceId);

    /// <summary>
    /// Resolves a <c>@handle</c> typed in chat to an agent in this workspace (REQ-RAG-021).
    /// </summary>
    /// <param name="workspaceId">The workspace the chat belongs to.</param>
    /// <param name="handle">The handle, with or without its leading '@'.</param>
    /// <returns>The agent, or null when the workspace has no agent with that handle.</returns>
    Task<AgentDefinition?> ResolveAsync(string workspaceId, string handle);

    /// <summary>
    /// Creates or updates an agent, rejecting a handle already used by a different agent in the
    /// same workspace.
    /// </summary>
    /// <param name="agent">The agent to persist.</param>
    /// <returns>The stored identifier.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the handle is already taken.</exception>
    Task<long> SaveAsync(AgentDefinition agent);

    /// <summary>Deletes a user-defined agent.</summary>
    /// <param name="workspaceId">The workspace the agent belongs to.</param>
    /// <param name="workspaceAgentId">The agent identifier.</param>
    /// <exception cref="InvalidOperationException">Thrown when the agent is the built-in one.</exception>
    Task DeleteAsync(string workspaceId, long workspaceAgentId);

    /// <summary>
    /// Resolves the skills an agent may actually call right now — the intersection of the workspace
    /// catalogue and the agent's own selection, computed fresh on every turn.
    /// </summary>
    /// <param name="agent">The agent about to run.</param>
    /// <returns>The permitted skill names, in catalogue order.</returns>
    Task<IReadOnlyList<string>> PermittedSkillsAsync(AgentDefinition agent);

    /// <summary>Records that an agent answered a turn.</summary>
    /// <param name="agent">The agent that ran.</param>
    Task MarkUsedAsync(AgentDefinition agent);
}
