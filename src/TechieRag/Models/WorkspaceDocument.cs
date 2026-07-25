namespace TechieRag.Models;

/// <summary>
/// A document's membership in a workspace, including its content hash (for embed-once
/// deduplication) and pin state (always-in-context).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The same underlying document (identified by content hash) can be
/// referenced from many workspaces without re-embedding. Stored in the TrWorkspaceDocument table.</para>
/// </remarks>
public class WorkspaceDocument
{
    /// <summary>Gets or sets the workspace identifier.</summary>
    public required string WorkspaceId { get; set; }

    /// <summary>Gets or sets the document identifier in the vector store.</summary>
    public required string DocumentId { get; set; }

    /// <summary>Gets or sets the SHA-256 hex hash of the document content, used for deduplication.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Gets or sets whether this document is pinned (always included in context).</summary>
    public bool IsPinned { get; set; }

    /// <summary>Gets or sets when the document was added to the workspace (UTC).</summary>
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
