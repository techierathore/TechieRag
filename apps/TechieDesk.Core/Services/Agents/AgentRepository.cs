using System.Data;
using Dapper;
using TechieDesk.Services.Data;

namespace TechieDesk.Services.Agents;

/// <summary>
/// Dapper implementation of <see cref="IAgentRepository"/> over the app database (REQ-UI-045).
/// </summary>
/// <remarks>
/// <para><b>Two tables, one aggregate.</b> An agent's skill selection lives in
/// <c>WorkspaceAgentSkill</c> rather than a delimited column, because it is joined against the
/// workspace catalogue on every turn (see <see cref="AgentSkillResolver"/>).</para>
/// <para><b>Explicit child deletes.</b> SQLite does not enforce foreign keys unless
/// <c>PRAGMA foreign_keys=ON</c> is set per connection, which this app does not set — so the skill
/// rows are removed by this code rather than by a declared cascade that would silently not fire and
/// leave orphans behind every delete.</para>
/// </remarks>
public sealed class AgentRepository : IAgentRepository
{
    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes the repository.</summary>
    /// <param name="connectionFactory">Provider-agnostic connection factory.</param>
    public AgentRepository(IAppDbConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        this.connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentDefinition>> ListAsync(string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        const string agentSql = """
            SELECT * FROM "WorkspaceAgent"
            WHERE "WorkspaceId" = @workspaceId
            ORDER BY "IsBuiltIn" DESC, "Handle" ASC;
            """;
        const string skillSql = """
            SELECT s."WorkspaceAgentId", s."SkillName"
            FROM "WorkspaceAgentSkill" s
            INNER JOIN "WorkspaceAgent" a ON a."WorkspaceAgentId" = s."WorkspaceAgentId"
            WHERE a."WorkspaceId" = @workspaceId;
            """;

        using var connection = connectionFactory.CreateConnection();
        var agents = (await connection.QueryAsync<AgentDefinition>(
            agentSql, new { workspaceId }).ConfigureAwait(false)).ToList();
        if (agents.Count == 0)
        {
            return agents;
        }

        var skills = await connection.QueryAsync<AgentSkillRow>(
            skillSql, new { workspaceId }).ConfigureAwait(false);
        Attach(agents, skills);
        return agents;
    }

    /// <inheritdoc />
    public async Task<AgentDefinition?> FindByHandleAsync(string workspaceId, string handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var normalized = AgentMentionParser.Normalize(handle);
        if (normalized.Length == 0)
        {
            return null;
        }

        const string sql = """
            SELECT * FROM "WorkspaceAgent"
            WHERE "WorkspaceId" = @workspaceId AND "Handle" = @normalized
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateConnection();
        var agent = await connection.QuerySingleOrDefaultAsync<AgentDefinition>(
            sql, new { workspaceId, normalized }).ConfigureAwait(false);
        if (agent is null)
        {
            return null;
        }

        var skills = await connection.QueryAsync<AgentSkillRow>(
            """SELECT "WorkspaceAgentId", "SkillName" FROM "WorkspaceAgentSkill" WHERE "WorkspaceAgentId" = @id;""",
            new { id = agent.WorkspaceAgentId }).ConfigureAwait(false);
        Attach([agent], skills);
        return agent;
    }

    /// <inheritdoc />
    public async Task<long> SaveAsync(AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(agent.WorkspaceId);

        agent.Handle = AgentMentionParser.Normalize(agent.Handle);
        if (!AgentMentionParser.IsValidHandle(agent.Handle))
        {
            throw new ArgumentException(
                "An agent handle must be letters, digits or hyphens.", nameof(agent));
        }

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var identifier = agent.WorkspaceAgentId == 0
            ? await InsertAsync(connection, transaction, agent).ConfigureAwait(false)
            : await UpdateAsync(connection, transaction, agent).ConfigureAwait(false);

        await connection.ExecuteAsync(
            """DELETE FROM "WorkspaceAgentSkill" WHERE "WorkspaceAgentId" = @identifier;""",
            new { identifier }, transaction).ConfigureAwait(false);

        foreach (var skill in agent.SelectedSkills.Where(SkillCatalog.Contains))
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO "WorkspaceAgentSkill" ("WorkspaceAgentId", "SkillName")
                VALUES (@identifier, @skill);
                """,
                new { identifier, skill }, transaction).ConfigureAwait(false);
        }

        transaction.Commit();
        agent.WorkspaceAgentId = identifier;
        return identifier;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long workspaceAgentId)
    {
        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """DELETE FROM "WorkspaceAgentSkill" WHERE "WorkspaceAgentId" = @workspaceAgentId;""",
            new { workspaceAgentId }, transaction).ConfigureAwait(false);
        await connection.ExecuteAsync(
            """DELETE FROM "WorkspaceAgent" WHERE "WorkspaceAgentId" = @workspaceAgentId;""",
            new { workspaceAgentId }, transaction).ConfigureAwait(false);

        transaction.Commit();
    }

    /// <inheritdoc />
    public async Task TouchAsync(long workspaceAgentId, DateTime usedAt)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """UPDATE "WorkspaceAgent" SET "LastUsedAt" = @usedAt WHERE "WorkspaceAgentId" = @workspaceAgentId;""",
            new { workspaceAgentId, usedAt }).ConfigureAwait(false);
    }

    private static async Task<long> InsertAsync(
        IDbConnection connection, IDbTransaction transaction, AgentDefinition agent)
    {
        const string sql = """
            INSERT INTO "WorkspaceAgent" (
                "WorkspaceId", "Handle", "DisplayName", "Description", "Instructions", "Model",
                "KnowledgeScope", "UsesEveryEnabledSkill", "RestrictToPinned", "AllowGeneralKnowledge",
                "MaxToolCalls", "TimeLimitSeconds", "ShowTrace", "ConfirmEgress", "AllowFollowUp",
                "IsBuiltIn", "CreatedAt", "UpdatedAt", "LastUsedAt")
            VALUES (
                @WorkspaceId, @Handle, @DisplayName, @Description, @Instructions, @Model,
                @KnowledgeScopeText, @UsesEveryEnabledSkill, @RestrictToPinned, @AllowGeneralKnowledge,
                @MaxToolCalls, @TimeLimitSeconds, @ShowTrace, @ConfirmEgress, @AllowFollowUp,
                @IsBuiltIn, @CreatedAt, @UpdatedAt, @LastUsedAt)
            RETURNING "WorkspaceAgentId";
            """;

        return await connection.ExecuteScalarAsync<long>(
            sql, Parameters(agent), transaction).ConfigureAwait(false);
    }

    private static async Task<long> UpdateAsync(
        IDbConnection connection, IDbTransaction transaction, AgentDefinition agent)
    {
        const string sql = """
            UPDATE "WorkspaceAgent" SET
                "Handle" = @Handle,
                "DisplayName" = @DisplayName,
                "Description" = @Description,
                "Instructions" = @Instructions,
                "Model" = @Model,
                "KnowledgeScope" = @KnowledgeScopeText,
                "UsesEveryEnabledSkill" = @UsesEveryEnabledSkill,
                "RestrictToPinned" = @RestrictToPinned,
                "AllowGeneralKnowledge" = @AllowGeneralKnowledge,
                "MaxToolCalls" = @MaxToolCalls,
                "TimeLimitSeconds" = @TimeLimitSeconds,
                "ShowTrace" = @ShowTrace,
                "ConfirmEgress" = @ConfirmEgress,
                "AllowFollowUp" = @AllowFollowUp,
                "UpdatedAt" = @UpdatedAt
            WHERE "WorkspaceAgentId" = @WorkspaceAgentId;
            """;

        await connection.ExecuteAsync(sql, Parameters(agent), transaction).ConfigureAwait(false);
        return agent.WorkspaceAgentId;
    }

    /// <summary>
    /// Flattens an agent into command parameters. The knowledge scope is written as its enum NAME
    /// rather than its ordinal, so reordering the enum can never silently repoint stored rows.
    /// </summary>
    /// <param name="agent">The agent being written.</param>
    /// <returns>The parameter object.</returns>
    private static object Parameters(AgentDefinition agent) => new
    {
        agent.WorkspaceAgentId,
        agent.WorkspaceId,
        agent.Handle,
        agent.DisplayName,
        agent.Description,
        agent.Instructions,
        agent.Model,
        KnowledgeScopeText = agent.KnowledgeScope.ToString(),
        agent.UsesEveryEnabledSkill,
        agent.RestrictToPinned,
        agent.AllowGeneralKnowledge,
        agent.MaxToolCalls,
        agent.TimeLimitSeconds,
        agent.ShowTrace,
        agent.ConfirmEgress,
        agent.AllowFollowUp,
        agent.IsBuiltIn,
        agent.CreatedAt,
        agent.UpdatedAt,
        agent.LastUsedAt
    };

    private static void Attach(IReadOnlyList<AgentDefinition> agents, IEnumerable<AgentSkillRow> skills)
    {
        var byIdentifier = agents.ToDictionary(a => a.WorkspaceAgentId);
        foreach (var row in skills)
        {
            if (byIdentifier.TryGetValue(row.WorkspaceAgentId, out var agent))
            {
                agent.SelectedSkills.Add(row.SkillName);
            }
        }
    }

    /// <summary>A row of the agent-to-skill join table.</summary>
    private sealed class AgentSkillRow
    {
        /// <summary>Gets or sets the owning agent identifier.</summary>
        public long WorkspaceAgentId { get; set; }

        /// <summary>Gets or sets the catalogue skill name.</summary>
        public string SkillName { get; set; } = string.Empty;
    }
}

