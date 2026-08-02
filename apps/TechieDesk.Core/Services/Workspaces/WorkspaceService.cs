using System.Text;
using TechieDesk.Services.Auth;
using TechieRag.Models;
using TechieRag.Services;

namespace TechieDesk.Services.Workspaces;

/// <summary>
/// Default <see cref="IWorkspaceService"/> implementation, a thin product-level facade over the
/// library <see cref="WorkspaceManager"/> (isolated documents + per-workspace retrieval/generation
/// settings).
/// </summary>
/// <remarks>
/// REQ-FN-041 (2026-07-26) removed both collaborators this class used to compose the manager with:
/// the app-DB <c>IWorkspaceAssignmentRepository</c> (user↔workspace membership) and the
/// <c>IAuthGuard</c> capability gate. TechieDesk is single-user and desktop-only — one install
/// serves one person, who is always the local owner — so there is nobody to assign workspaces to
/// and nobody to authorize against. Crucially, <see cref="ListForCurrentUserAsync"/> now returns
/// the manager's full list unconditionally: the old code took that branch only for a caller holding
/// <c>ManageAllWorkspaces</c> and otherwise intersected with the (now non-existent) assignment
/// rows, which on a single-user install would silently hide every workspace.
/// </remarks>
public sealed class WorkspaceService : IWorkspaceService
{
    private readonly TechieRagManager rag;
    private readonly ITechieDeskUserContext userContext;

    /// <summary>Initializes the workspace service.</summary>
    /// <param name="rag">The RAG manager exposing the library workspace manager.</param>
    /// <param name="userContext">The current-user context.</param>
    public WorkspaceService(TechieRagManager rag, ITechieDeskUserContext userContext)
    {
        this.rag = rag ?? throw new ArgumentNullException(nameof(rag));
        this.userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    /// <inheritdoc />
    public string CurrentUserId => userContext.CurrentUser.UserId.ToString();

    /// <inheritdoc />
    public bool CanManageWorkspaces => true;

    /// <inheritdoc />
    public bool CanTuneRetrieval => true;

    /// <inheritdoc />
    public string SlugFor(Workspace workspace) => Slugify(workspace.Name);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Workspace>> ListForCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var manager = await RequireManagerAsync(cancellationToken).ConfigureAwait(false);

        // REQ-FN-041: every workspace, always. The local owner owns the whole store.
        return await manager.ListWorkspacesAsync(cancellationToken).ConfigureAwait(false);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var manager = await RequireManagerAsync(cancellationToken).ConfigureAwait(false);
        return await manager.CreateWorkspaceAsync(name.Trim(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var manager = await RequireManagerAsync(cancellationToken).ConfigureAwait(false);
        workspace.UpdatedAt = DateTime.UtcNow;
        await manager.UpdateWorkspaceAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RenameWorkspaceAsync(string workspaceId, string newName, CancellationToken cancellationToken = default)
    {
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
        var manager = await RequireManagerAsync(cancellationToken).ConfigureAwait(false);
        await manager.DeleteWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> EnsureDefaultWorkspaceAsync(string userId, CancellationToken cancellationToken = default)
    {
        _ = userId;

        var manager = await RequireManagerAsync(cancellationToken).ConfigureAwait(false);
        var existing = await manager.ListWorkspacesAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return false;
        }

        await manager.CreateWorkspaceAsync("Default", w =>
        {
            w.SystemPrompt = "You are a helpful assistant. Answer using the workspace's documents when relevant.";
            w.ChatMode = WorkspaceChatMode.Chat;
        }, cancellationToken).ConfigureAwait(false);

        return true;
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
