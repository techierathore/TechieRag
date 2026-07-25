using Dapper;

namespace TechieDesk.Services.Data;

/// <summary>
/// Dapper implementation of <see cref="IWorkspaceAssignmentRepository"/> using
/// parameterized SQL portable across SQLite and PostgreSQL (BRD-102).
/// </summary>
public sealed class WorkspaceAssignmentRepository : IWorkspaceAssignmentRepository
{
    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes the repository.</summary>
    /// <param name="connectionFactory">Provider-agnostic connection factory.</param>
    public WorkspaceAssignmentRepository(IAppDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<long> CreateAsync(WorkspaceAssignment assignment)
    {
        if (assignment.CreatedAt == default)
        {
            assignment.CreatedAt = DateTime.UtcNow;
        }

        const string sql = """
            INSERT INTO "WorkspaceAssignment" ("WorkspaceId", "UserId", "RoleName", "CreatedAt")
            VALUES (@WorkspaceId, @UserId, @RoleName, @CreatedAt)
            RETURNING "WorkspaceAssignmentId";
            """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(sql, assignment).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WorkspaceAssignment?> GetAsync(long workspaceAssignmentId)
    {
        const string sql = """
            SELECT * FROM "WorkspaceAssignment"
            WHERE "WorkspaceAssignmentId" = @workspaceAssignmentId;
            """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<WorkspaceAssignment>(
            sql, new { workspaceAssignmentId }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceAssignment>> GetByWorkspaceAsync(string workspaceId)
    {
        const string sql = """
            SELECT * FROM "WorkspaceAssignment"
            WHERE "WorkspaceId" = @workspaceId
            ORDER BY "CreatedAt";
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<WorkspaceAssignment>(
            sql, new { workspaceId }).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceAssignment>> GetByUserAsync(string userId)
    {
        const string sql = """
            SELECT * FROM "WorkspaceAssignment"
            WHERE "UserId" = @userId
            ORDER BY "CreatedAt";
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<WorkspaceAssignment>(
            sql, new { userId }).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<bool> UpdateRoleAsync(long workspaceAssignmentId, string roleName)
    {
        const string sql = """
            UPDATE "WorkspaceAssignment"
            SET "RoleName" = @roleName
            WHERE "WorkspaceAssignmentId" = @workspaceAssignmentId;
            """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            sql, new { workspaceAssignmentId, roleName }).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long workspaceAssignmentId)
    {
        const string sql = """
            DELETE FROM "WorkspaceAssignment"
            WHERE "WorkspaceAssignmentId" = @workspaceAssignmentId;
            """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            sql, new { workspaceAssignmentId }).ConfigureAwait(false);
        return affected > 0;
    }
}
