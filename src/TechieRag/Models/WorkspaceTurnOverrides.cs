namespace TechieRag.Models;

/// <summary>
/// Restricts which of a workspace's documents a single turn is allowed to retrieve from.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> BRD-137 / REQ-UI-044 — the composer's retrieval-scope picker. Workspace
/// isolation is still the outer boundary: a scope can only ever narrow the workspace's document
/// set, never reach documents outside it.</para>
/// </remarks>
public enum WorkspaceRetrievalScope
{
    /// <summary>Retrieve from every document in the workspace (the default).</summary>
    WholeWorkspace,

    /// <summary>Retrieve only from the workspace's pinned documents.</summary>
    PinnedOnly,

    /// <summary>Retrieve only from an explicitly chosen subset of the workspace's documents.</summary>
    SelectedDocuments
}

/// <summary>
/// Per-turn overrides applied on top of a workspace's stored settings for one ask/stream call.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> BRD-137 / REQ-UI-044. The answering mode, model and retrieval scope are
/// chosen at the turn rather than only per workspace, so a user can ask one question in strict
/// Query mode against pinned documents without permanently changing the workspace.</para>
/// <para><b>Scoping:</b> These values are read once per call and are never written back to the
/// workspace store, so they cannot leak into the next turn.</para>
/// <para><b>Code Flow:</b> Passed to
/// <c>WorkspaceManager.AskTurnStreamAsync</c> / <c>WorkspaceManager.SearchScopedAsync</c>; a null
/// instance means "use the workspace's stored settings unchanged".</para>
/// </remarks>
public class WorkspaceTurnOverrides
{
    /// <summary>Gets or sets the answer mode for this turn, or null to use the workspace's stored mode.</summary>
    public WorkspaceChatMode? ChatMode { get; set; }

    /// <summary>Gets or sets the model for this turn, or null to use the workspace/provider model.</summary>
    public string? LlmModel { get; set; }

    /// <summary>Gets or sets the retrieval scope for this turn.</summary>
    public WorkspaceRetrievalScope Scope { get; set; } = WorkspaceRetrievalScope.WholeWorkspace;

    /// <summary>
    /// Gets or sets the chosen document identifiers, honored only when
    /// <see cref="Scope"/> is <see cref="WorkspaceRetrievalScope.SelectedDocuments"/>.
    /// </summary>
    /// <remarks>Identifiers that are not members of the workspace are ignored; an empty or null
    /// list under <see cref="WorkspaceRetrievalScope.SelectedDocuments"/> retrieves nothing.</remarks>
    public IReadOnlyList<string>? DocumentIds { get; set; }
}
