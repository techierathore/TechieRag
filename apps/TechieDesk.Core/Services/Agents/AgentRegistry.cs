using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Agents;

/// <summary>
/// Default <see cref="IAgentRegistry"/>: the agent rules over the Dapper repositories.
/// </summary>
/// <remarks>
/// <para><b>Built-in <c>@agent</c>.</b> A workspace always has one, and it is never deletable. It
/// is not seeded into the database on workspace creation — a synthesized row means an install that
/// predates this feature, and a workspace created by any other path, both behave identically
/// without a backfill script. It materialises into a real row the first time it is edited.</para>
/// <para><b>Handle collisions are rejected here, not by the database.</b> The unique constraint is
/// the backstop; catching it early is what lets the editor say "@analyst is already taken" instead
/// of surfacing a constraint violation.</para>
/// <para><b>Skills are intersected on every call.</b> <see cref="PermittedSkillsAsync"/> re-reads
/// the catalogue rather than trusting anything cached on the agent, which is what makes revoking a
/// catalogue skill take effect immediately for every agent.</para>
/// </remarks>
public sealed class AgentRegistry : IAgentRegistry
{
    private readonly IAgentRepository agents;
    private readonly IWorkspaceSkillRepository skills;
    private readonly LocalizeText localize;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the registry.</summary>
    /// <param name="agents">Agent persistence.</param>
    /// <param name="skills">Workspace skill-catalogue persistence.</param>
    /// <param name="localize">Resolves the refusal sentences the agent editor toasts (REQ-UI-055).</param>
    /// <param name="timeProvider">Clock used for created/updated/last-used stamps.</param>
    /// <remarks>
    /// REQ-UI-055 / BRD-91: the three <see cref="InvalidOperationException"/> messages below are not
    /// developer text. The agent editor catches them and puts <c>ex.Message</c> straight into a
    /// toast body, so on a Hindi install they were the only English on the screen. The HANDLE and
    /// the clashing display name inside them stay verbatim — they are what the user typed.
    /// </remarks>
    public AgentRegistry(
        IAgentRepository agents,
        IWorkspaceSkillRepository skills,
        LocalizeText localize,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(localize);

        this.agents = agents;
        this.skills = skills;
        this.localize = localize;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentDefinition>> ListAsync(string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var stored = await agents.ListAsync(workspaceId).ConfigureAwait(false);
        if (stored.Any(a => a.IsBuiltIn))
        {
            return stored;
        }

        var list = new List<AgentDefinition>(stored.Count + 1)
        {
            AgentDefinition.BuiltIn(workspaceId, UtcNow())
        };
        list.AddRange(stored);
        return list;
    }

    /// <inheritdoc />
    public async Task<AgentDefinition?> ResolveAsync(string workspaceId, string handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var normalized = AgentMentionParser.Normalize(handle);
        if (normalized.Length == 0)
        {
            return null;
        }

        var stored = await agents.FindByHandleAsync(workspaceId, normalized).ConfigureAwait(false);
        if (stored is not null)
        {
            return stored;
        }

        // The built-in agent answers to @agent even before it has ever been edited into a row.
        return normalized == AgentDefinition.BuiltInHandle
            ? AgentDefinition.BuiltIn(workspaceId, UtcNow())
            : null;
    }

    /// <inheritdoc />
    public async Task<long> SaveAsync(AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        agent.Handle = AgentMentionParser.Normalize(agent.Handle);
        if (!AgentMentionParser.IsValidHandle(agent.Handle))
        {
            throw new InvalidOperationException(localize("AgentsInvalidHandle"));
        }

        if (string.IsNullOrWhiteSpace(agent.DisplayName))
        {
            throw new InvalidOperationException(localize("AgentsDisplayNameRequired"));
        }

        var clash = await agents.FindByHandleAsync(agent.WorkspaceId, agent.Handle).ConfigureAwait(false);
        if (clash is not null && clash.WorkspaceAgentId != agent.WorkspaceAgentId)
        {
            throw new InvalidOperationException(
                localize("AgentsHandleAlreadyUsed", agent.Handle, clash.DisplayName));
        }

        var now = UtcNow();
        if (agent.CreatedAt == default)
        {
            agent.CreatedAt = now;
        }
        agent.UpdatedAt = now;
        agent.MaxToolCalls = Math.Clamp(agent.MaxToolCalls, 1, MaxToolCallCeiling);
        agent.TimeLimitSeconds = Math.Clamp(agent.TimeLimitSeconds, 5, TimeLimitCeiling);

        return await agents.SaveAsync(agent).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string workspaceId, long workspaceAgentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var stored = await agents.ListAsync(workspaceId).ConfigureAwait(false);
        var target = stored.FirstOrDefault(a => a.WorkspaceAgentId == workspaceAgentId);
        if (target is null)
        {
            return;
        }

        if (target.IsBuiltIn)
        {
            throw new InvalidOperationException(localize("AgentsBuiltInNotDeletable"));
        }

        await agents.DeleteAsync(workspaceAgentId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PermittedSkillsAsync(AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var catalogue = await skills.GetCatalogueAsync(agent.WorkspaceId).ConfigureAwait(false);
        return AgentSkillResolver.Permitted(catalogue, agent);
    }

    /// <inheritdoc />
    public async Task MarkUsedAsync(AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (agent.WorkspaceAgentId == 0)
        {
            // The synthesized built-in has no row to stamp; it materialises on first edit.
            return;
        }

        var now = UtcNow();
        agent.LastUsedAt = now;
        await agents.TouchAsync(agent.WorkspaceAgentId, now).ConfigureAwait(false);
    }

    /// <summary>The highest per-turn tool-call ceiling an agent may be configured with.</summary>
    public const int MaxToolCallCeiling = 50;

    /// <summary>The longest per-run time limit an agent may be configured with, in seconds.</summary>
    public const int TimeLimitCeiling = 900;

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
