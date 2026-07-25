using TechieRag.Models;

namespace TechieDesk.Services.Workspaces;

/// <summary>
/// App-level facade over the TechieRag library <c>WorkspaceManager</c> and the app-DB
/// <c>WorkspaceAssignment</c> store (Wave 3). Adds the product concerns the library does not
/// own: URL slugs, per-user visibility scoping (REQ-FN-008), role-gated create/rename/delete
/// (REQ-UI-014), default-workspace bootstrap (REQ-FN-009), and workspace membership editing.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>Gets the current user identifier used for assignments and threads.</summary>
    string CurrentUserId { get; }

    /// <summary>Gets whether the current user may create/rename/delete workspaces and edit settings.</summary>
    bool CanManageWorkspaces { get; }

    /// <summary>Gets whether the current user may tune retrieval settings.</summary>
    bool CanTuneRetrieval { get; }

    /// <summary>Gets whether the current user may add/remove workspace members.</summary>
    bool CanManageMembers { get; }

    /// <summary>Computes the URL slug for a workspace (slugified display name).</summary>
    /// <param name="workspace">The workspace.</param>
    /// <returns>A lowercase hyphenated slug.</returns>
    string SlugFor(Workspace workspace);

    /// <summary>
    /// Lists the workspaces the current user may see: every workspace for a user who can manage
    /// all workspaces (Admin), otherwise only the workspaces the user is assigned to (REQ-FN-008).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Visible workspaces, most-recently-updated first.</returns>
    Task<IReadOnlyList<Workspace>> ListForCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a visible workspace by its URL slug.</summary>
    /// <param name="slug">The URL slug.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The workspace, or null when not found or not visible to the current user.</returns>
    Task<Workspace?> ResolveBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Creates a workspace and assigns the current user to it (role-gated).</summary>
    /// <param name="name">The display name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The created workspace.</returns>
    Task<Workspace> CreateWorkspaceAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Persists updated workspace settings (role-gated).</summary>
    /// <param name="workspace">The workspace with updated values.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UpdateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default);

    /// <summary>Renames a workspace (role-gated).</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="newName">The new display name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RenameWorkspaceAsync(string workspaceId, string newName, CancellationToken cancellationToken = default);

    /// <summary>Deletes a workspace and its memberships (role-gated).</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures at least one workspace exists on first run, creating a "Default" workspace and
    /// assigning the given user when the store is empty (REQ-FN-009).
    /// </summary>
    /// <param name="userId">The user to assign to the freshly created default workspace.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True when a default workspace was created; false when workspaces already existed.</returns>
    Task<bool> EnsureDefaultWorkspaceAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Lists a workspace's members (assignments).</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <returns>The workspace's member assignments.</returns>
    Task<IReadOnlyList<WorkspaceMember>> GetMembersAsync(string workspaceId);

    /// <summary>Adds a member to a workspace (role-gated).</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="userId">The user identifier to add.</param>
    /// <param name="roleName">The role within the workspace.</param>
    Task AddMemberAsync(string workspaceId, string userId, string roleName);

    /// <summary>Removes a member assignment (role-gated).</summary>
    /// <param name="assignmentId">The assignment primary key.</param>
    Task RemoveMemberAsync(long assignmentId);
}

/// <summary>A workspace member row for the settings members editor.</summary>
/// <param name="AssignmentId">The assignment primary key.</param>
/// <param name="UserId">The member's user identifier.</param>
/// <param name="RoleName">The member's role within the workspace.</param>
public sealed record WorkspaceMember(long AssignmentId, string UserId, string RoleName);
