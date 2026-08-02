using Dapper;
using TechieDesk.Services.Data;

namespace TechieDesk.Services.Agents;

/// <summary>
/// Dapper implementation of <see cref="IWorkspaceSkillRepository"/> (REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>Absent means default, not off.</b> A workspace only gets rows for skills the owner has
/// actually touched, and reads merge those over <see cref="SkillCatalog.Defaults"/>. Treating a
/// missing row as "off" would have left every existing workspace with no skills at all the moment
/// this table shipped — including RAG search, which is the one the product is built around.</para>
/// <para><b>Unknown names are ignored.</b> Only names in the catalogue are written, so a stale row
/// from a removed skill can never re-enable something the build no longer defines.</para>
/// </remarks>
public sealed class WorkspaceSkillRepository : IWorkspaceSkillRepository
{
    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes the repository.</summary>
    /// <param name="connectionFactory">Provider-agnostic connection factory.</param>
    public WorkspaceSkillRepository(IAppDbConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        this.connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, bool>> GetCatalogueAsync(string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        const string sql = """
            SELECT "SkillName", "IsEnabled" FROM "WorkspaceSkill" WHERE "WorkspaceId" = @workspaceId;
            """;

        var catalogue = SkillCatalog.Defaults();

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SkillToggleRow>(
            sql, new { workspaceId }).ConfigureAwait(false);

        foreach (var row in rows.Where(r => SkillCatalog.Contains(r.SkillName)))
        {
            catalogue[row.SkillName] = row.IsEnabled;
        }

        return catalogue;
    }

    /// <inheritdoc />
    public async Task SetAsync(string workspaceId, string skillName, bool isEnabled, DateTime updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        if (!SkillCatalog.Contains(skillName))
        {
            throw new ArgumentException($"'{skillName}' is not a catalogue skill.", nameof(skillName));
        }

        const string sql = """
            INSERT INTO "WorkspaceSkill" ("WorkspaceId", "SkillName", "IsEnabled", "UpdatedAt")
            VALUES (@workspaceId, @skillName, @isEnabled, @updatedAt)
            ON CONFLICT ("WorkspaceId", "SkillName")
            DO UPDATE SET "IsEnabled" = @isEnabled, "UpdatedAt" = @updatedAt;
            """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            sql, new { workspaceId, skillName, isEnabled, updatedAt }).ConfigureAwait(false);
    }

    /// <summary>A stored catalogue toggle.</summary>
    private sealed class SkillToggleRow
    {
        /// <summary>Gets or sets the catalogue skill name.</summary>
        public string SkillName { get; set; } = string.Empty;

        /// <summary>Gets or sets whether the workspace permits the skill.</summary>
        public bool IsEnabled { get; set; }
    }
}
