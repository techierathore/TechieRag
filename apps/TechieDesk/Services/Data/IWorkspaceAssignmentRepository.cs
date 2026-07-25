namespace TechieDesk.Services.Data;

/// <summary>
/// CRUD access to <see cref="WorkspaceAssignment"/> rows (Dapper-only, BRD-102).
/// </summary>
public interface IWorkspaceAssignmentRepository
{
    /// <summary>Creates an assignment and returns its new primary key.</summary>
    /// <param name="assignment">The assignment to insert; <c>CreatedAt</c> defaults to now (UTC) when unset.</param>
    /// <returns>The generated <c>WorkspaceAssignmentId</c>.</returns>
    Task<long> CreateAsync(WorkspaceAssignment assignment);

    /// <summary>Gets an assignment by primary key.</summary>
    /// <param name="workspaceAssignmentId">The primary key.</param>
    /// <returns>The assignment, or null when not found.</returns>
    Task<WorkspaceAssignment?> GetAsync(long workspaceAssignmentId);

    /// <summary>Lists all assignments for a workspace.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <returns>Assignments ordered by creation time.</returns>
    Task<IReadOnlyList<WorkspaceAssignment>> GetByWorkspaceAsync(string workspaceId);

    /// <summary>Lists all assignments for a user.</summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>Assignments ordered by creation time.</returns>
    Task<IReadOnlyList<WorkspaceAssignment>> GetByUserAsync(string userId);

    /// <summary>Updates the role of an existing assignment.</summary>
    /// <param name="workspaceAssignmentId">The primary key.</param>
    /// <param name="roleName">The new role name.</param>
    /// <returns>True when a row was updated.</returns>
    Task<bool> UpdateRoleAsync(long workspaceAssignmentId, string roleName);

    /// <summary>Deletes an assignment.</summary>
    /// <param name="workspaceAssignmentId">The primary key.</param>
    /// <returns>True when a row was deleted.</returns>
    Task<bool> DeleteAsync(long workspaceAssignmentId);
}
