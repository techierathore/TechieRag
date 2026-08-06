using TechieRag.Models;

namespace TechieDesk.Services.Workspaces;

/// <summary>
/// App-level facade over the TechieRag library <c>WorkspaceManager</c> (Wave 3). Adds the product
/// concerns the library does not own: URL slugs, default-workspace bootstrap (REQ-FN-009), and
/// create/rename/delete plumbing for the shell.
/// </summary>
/// <remarks>
/// REQ-FN-041 (2026-07-26): the user↔workspace assignment store and the role/capability guard are
/// gone. TechieDesk is a single-user desktop app — one install, one owner — so there is no
/// visibility scoping (REQ-FN-008, retired) and no membership editing. Every workspace in the
/// store belongs to the person at the keyboard and is listed unconditionally.
/// </remarks>
public interface IWorkspaceService
{
    /// <summary>Gets the current user identifier used for threads.</summary>
    string CurrentUserId { get; }

    /// <summary>
    /// Gets whether the current user may create/rename/delete workspaces and edit settings.
    /// Always true on the single-user desktop; retained so the shell keeps one place to ask.
    /// </summary>
    bool CanManageWorkspaces { get; }

    /// <summary>
    /// Gets whether the current user may tune retrieval settings. Always true on the single-user
    /// desktop; retained so the shell keeps one place to ask.
    /// </summary>
    bool CanTuneRetrieval { get; }

    /// <summary>Computes the URL slug for a workspace (slugified display name).</summary>
    /// <param name="workspace">The workspace.</param>
    /// <returns>A lowercase hyphenated slug.</returns>
    string SlugFor(Workspace workspace);

    /// <summary>
    /// Lists every workspace in the store. One desktop install serves one person, so there is
    /// nothing to scope the list by (REQ-FN-041).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>All workspaces, most-recently-updated first.</returns>
    Task<IReadOnlyList<Workspace>> ListForCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a workspace by its URL slug.</summary>
    /// <param name="slug">The URL slug.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The workspace, or null when not found.</returns>
    Task<Workspace?> ResolveBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Creates a workspace.</summary>
    /// <param name="name">The display name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The created workspace.</returns>
    Task<Workspace> CreateWorkspaceAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Persists updated workspace settings.</summary>
    /// <param name="workspace">The workspace with updated values.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UpdateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default);

    /// <summary>Renames a workspace.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="newName">The new display name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RenameWorkspaceAsync(string workspaceId, string newName, CancellationToken cancellationToken = default);

    /// <summary>Deletes a workspace.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures at least one workspace exists on first run, creating a "Default" workspace when the
    /// store is empty (REQ-FN-009).
    /// </summary>
    /// <param name="userId">The owner identifier, kept for call-site compatibility and logging.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True when a default workspace was created; false when workspaces already existed.</returns>
    Task<bool> EnsureDefaultWorkspaceAsync(string userId, CancellationToken cancellationToken = default);
}
