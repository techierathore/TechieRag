using TechieRag.Services;

namespace TechieDesk.Services.Web;

/// <summary>
/// Adds an already-ingested document to a workspace (REQ-RAG-016/017/018).
/// </summary>
/// <remarks>
/// The library's web-ingestion extensions hang off <see cref="TechieRag.ITechieRag"/>, so what they
/// produce is a document in the global catalogue with no workspace membership — nothing the
/// Documents screen would ever list. This is the second half of the composition, kept behind an
/// interface so the ingestion service can be tested without standing up the whole manager.
/// </remarks>
public interface IWorkspaceDocumentLinker
{
    /// <summary>References an existing document from a workspace.</summary>
    /// <param name="workspaceId">The workspace to add it to.</param>
    /// <param name="documentId">The document already present in the catalogue.</param>
    /// <param name="pinned">Whether the document is kept in workspace context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the link was written; false when persistence is not configured.</returns>
    Task<bool> LinkAsync(
        string workspaceId,
        string documentId,
        bool pinned,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Links documents into workspaces through the library's <see cref="WorkspaceManager"/>.
/// </summary>
public sealed class WorkspaceDocumentLinker : IWorkspaceDocumentLinker
{
    private readonly TechieRagManager rag;
    private readonly ILogger<WorkspaceDocumentLinker> logger;

    /// <summary>Initializes a new instance of the <see cref="WorkspaceDocumentLinker"/> class.</summary>
    /// <param name="rag">Owns the workspace manager's lifecycle.</param>
    /// <param name="logger">Diagnostics.</param>
    public WorkspaceDocumentLinker(TechieRagManager rag, ILogger<WorkspaceDocumentLinker> logger)
    {
        this.rag = rag ?? throw new ArgumentNullException(nameof(rag));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> LinkAsync(
        string workspaceId,
        string documentId,
        bool pinned,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        var manager = await rag.GetWorkspaceManagerAsync().ConfigureAwait(false);
        if (manager is null)
        {
            logger.LogWarning(
                "Workspace persistence is not configured, so {DocumentId} was ingested but not added to {WorkspaceId}",
                documentId,
                workspaceId);
            return false;
        }

        // No content hash is recorded: the library extension hands back an id and keeps the text,
        // so the hash a file upload would have written is not available here. The consequence is
        // bounded and worth stating — a later upload of byte-identical content will not dedupe
        // against this row, and will produce a second document rather than reusing this one.
        await manager
            .AddExistingDocumentAsync(workspaceId, documentId, contentHash: null, pinned, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
