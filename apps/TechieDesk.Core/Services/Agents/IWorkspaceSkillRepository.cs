namespace TechieDesk.Services.Agents;

/// <summary>
/// Persistence for the per-workspace skill catalogue — the outer permission boundary an agent
/// selects from and can never widen (BRD-84 / REQ-RAG-022).
/// </summary>
public interface IWorkspaceSkillRepository
{
    /// <summary>
    /// Reads a workspace's catalogue, merged over the shipped defaults so every catalogue entry is
    /// present even in a workspace that has never been configured.
    /// </summary>
    /// <param name="workspaceId">The workspace.</param>
    /// <returns>Skill name to enabled, covering the whole catalogue.</returns>
    Task<IReadOnlyDictionary<string, bool>> GetCatalogueAsync(string workspaceId);

    /// <summary>Turns one catalogue skill on or off for a workspace.</summary>
    /// <param name="workspaceId">The workspace.</param>
    /// <param name="skillName">The catalogue skill name.</param>
    /// <param name="isEnabled">Whether the workspace permits it.</param>
    /// <param name="updatedAt">When the toggle changed (UTC).</param>
    Task SetAsync(string workspaceId, string skillName, bool isEnabled, DateTime updatedAt);
}
