using System.Text;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Data;
using TechieRag.Models;
using TechieRag.Services;

namespace TechieDesk.Services.Workspaces;

/// <summary>
/// Default <see cref="IWorkspaceService"/> implementation. Composes the library
/// <see cref="WorkspaceManager"/> (isolated documents + per-workspace retrieval/generation
/// settings) with the app-DB <see cref="IWorkspaceAssignmentRepository"/> (user↔workspace
/// membership) and the server-side <see cref="IAuthGuard"/> so every mutating call is
/// authorized regardless of UI state (BRD-25).
/// </summary>
public sealed class WorkspaceService : IWorkspaceService
{
    private readonly TechieRagManager rag;
    private readonly IWorkspaceAssignmentRepository assignments;
    private readonly IAuthGuard authGuard;
    private readonly ITechieDeskUserContext userContext;

    /// <summary>Initializes the workspace service.</summary>
    /// <param name="rag">The RAG manager exposing the library workspace manager.</param>
    /// <param name="assignments">The user↔workspace assignment repository.</param>
    /// <param name="authGuard">The server-side authorization guard.</param>
    /// <param name="userContext">The current-user context.</param>
    public WorkspaceService(
        TechieRagManager rag,
        IWorkspaceAssignmentRepository assignments,
        IAuthGuard authGuard,
        ITechieDeskUserContext userContext)
    {
        this.rag = rag;
        this.assignments = assignments;
        this.authGuard = authGuard;
        this.userContext = userContext;
    }

    /// <inheritdoc />
    public string CurrentUserId => userContext.CurrentUser.UserId.ToString();

    /// <inheritdoc />
    public bool CanManageWorkspaces => authGuard.Allows(Capability.ManageWorkspaces);

    /// <inheritdoc />
    public bool CanTuneRetrieval => authGuard.Allows(Capability.TuneRetrieval);

    /// <inheritdoc />
    public bool CanManageMembers => authGuard.Allows(Capability.AssignUsersToWorkspaces);

    /// <inheritdoc />
    public string SlugFor(Workspace workspace) => Slugify(workspace.Name);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Workspace>> ListForCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var manager = await RequireManagerAsync(cancellationToken).ConfigureAwait(false);
        var all = await manager.ListWorkspacesAsync(cancellationToken).ConfigureAwait(false);

        // A user who can see every workspace (Admin) gets the full list; everyone else is
        // scoped to the workspaces they are assigned to in the app DB (REQ-FN-008).
        if (authGuard.Allows(Capability.ManageAllWorkspaces))
        {
            return all;
        }

        var assigned = await assignments.GetByUserAsync(CurrentUserId).ConfigureAwait(false);
        var assignedIds = assigned.Select(a => a.WorkspaceId).ToHashSet(StringComparer.Ordinal);
        return all.Where(w => assignedIds.Contains(w.WorkspaceId)).ToList();
    }

    /// <inheritdoc />
    public async Task<Workspace?> ResolveBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var visible = await ListForCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        return visible.FirstOrDefault(w => string.Equals(SlugFor(w), slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<Workspace> CreateWorkspaceAsync(string name, CancellationToken cancellationToken = default)
    {
        authGuard.Require(Capability.ManageWorkspaces);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var manager = await RequireManagerAsync(cancellationToken).ConfigureAwait(false);
        var workspace = await manager.CreateWorkspaceAsync(name.Trim(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Assign the creator so it is visible to them even without ManageAllWorkspaces.
        await assignments.CreateAsync(new WorkspaceAssignment
        {
            WorkspaceId = workspace.WorkspaceId,
            UserId = CurrentUserId,
            RoleName = userContext.CurrentUser.Role.ToString()
        }).ConfigureAwait(false);

        return workspace;
    }

    /// <inheritdoc />
    public async Task UpdateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        authGuard.Require(Capability.ManageWorkspaces);
        ArgumentNullException.ThrowIfNull(workspace);

        var manager = await RequireManagerAsync(cancellationToken).ConfigureAwait(false);
        workspace.UpdatedAt = DateTime.UtcNow;
        await manager.UpdateWorkspaceAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RenameWorkspaceAsync(string workspaceId, string newName, CancellationToken cancellationToken = default)
    {
        authGuard.Require(Capability.ManageWorkspaces);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var manager = await RequireManagerAsync(cancellationToken).ConfigureAwait(false);
        var workspace = await manager.GetWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' does not exist.");
        workspace.Name = newName.Trim();
        workspace.UpdatedAt = DateTime.UtcNow;
        await manager.UpdateWorkspaceAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        authGuard.Require(Capability.ManageWorkspaces);

        var manager = await RequireManagerAsync(cancellationToken).ConfigureAwait(false);
        await manager.DeleteWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);

        // Clean up the app-DB memberships for the removed workspace.
        var members = await assignments.GetByWorkspaceAsync(workspaceId).ConfigureAwait(false);
        foreach (var member in members)
        {
            await assignments.DeleteAsync(member.WorkspaceAssignmentId).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> EnsureDefaultWorkspaceAsync(string userId, CancellationToken cancellationToken = default)
    {
        var manager = await RequireManagerAsync(cancellationToken).ConfigureAwait(false);
        var existing = await manager.ListWorkspacesAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return false;
        }

        var workspace = await manager.CreateWorkspaceAsync("Default", w =>
        {
            w.SystemPrompt = "You are a helpful assistant. Answer using the workspace's documents when relevant.";
            w.ChatMode = WorkspaceChatMode.Chat;
        }, cancellationToken).ConfigureAwait(false);

        await assignments.CreateAsync(new WorkspaceAssignment
        {
            WorkspaceId = workspace.WorkspaceId,
            UserId = userId,
            RoleName = ProductRole.Admin.ToString()
        }).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceMember>> GetMembersAsync(string workspaceId)
    {
        var rows = await assignments.GetByWorkspaceAsync(workspaceId).ConfigureAwait(false);
        return rows.Select(r => new WorkspaceMember(r.WorkspaceAssignmentId, r.UserId, r.RoleName)).ToList();
    }

    /// <inheritdoc />
    public async Task AddMemberAsync(string workspaceId, string userId, string roleName)
    {
        authGuard.Require(Capability.AssignUsersToWorkspaces);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var existing = await assignments.GetByWorkspaceAsync(workspaceId).ConfigureAwait(false);
        if (existing.Any(a => string.Equals(a.UserId, userId, StringComparison.Ordinal)))
        {
            return; // already a member — idempotent
        }

        await assignments.CreateAsync(new WorkspaceAssignment
        {
            WorkspaceId = workspaceId,
            UserId = userId.Trim(),
            RoleName = string.IsNullOrWhiteSpace(roleName) ? ProductRole.User.ToString() : roleName
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveMemberAsync(long assignmentId)
    {
        authGuard.Require(Capability.AssignUsersToWorkspaces);
        await assignments.DeleteAsync(assignmentId).ConfigureAwait(false);
    }

    private async Task<WorkspaceManager> RequireManagerAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return await rag.GetWorkspaceManagerAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Workspace persistence is not configured. Ensure TechieRag is wired with WithPersistence.");
    }

    /// <summary>
    /// Produces a URL-safe slug from a workspace name: lowercased, non-alphanumeric runs
    /// collapsed to single hyphens, trimmed. Empty results fall back to "workspace".
    /// </summary>
    private static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "workspace";

        var builder = new StringBuilder(name.Length);
        var lastWasHyphen = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "workspace" : slug;
    }
}
