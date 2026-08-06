using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for persistent workspace/collection storage.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Persists workspaces (isolated document collections with their own
/// retrieval settings) and their document memberships, including content-hash based
/// deduplication and document pinning.</para>
/// <para><b>Code Flow:</b> Configured via TechieRagBuilder.WithPersistence. The library owns
/// and self-creates its tables (TrWorkspace, TrWorkspaceDocument) on <see cref="InitializeAsync"/>.</para>
/// <para><b>Implementations:</b> SqliteWorkspaceStore, PostgresWorkspaceStore.</para>
/// </remarks>
public interface IWorkspaceStore
{
    /// <summary>
    /// Initializes the store, creating the TrWorkspace and TrWorkspaceDocument tables
    /// if they do not exist.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous initialization.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new workspace.
    /// </summary>
    /// <param name="workspace">The workspace to create.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The created workspace.</returns>
    Task<Workspace> CreateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a workspace by identifier.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The workspace, or null when it does not exist.</returns>
    Task<Workspace?> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all workspaces ordered by most recently updated first.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>All workspaces, newest first.</returns>
    Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing workspace's settings.
    /// </summary>
    /// <param name="workspace">The workspace with updated values.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous update.</returns>
    Task UpdateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a workspace and its document memberships (documents themselves are not deleted).
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete.</returns>
    Task DeleteWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds (or updates) a document membership in a workspace.
    /// </summary>
    /// <param name="document">The membership record including content hash and pin state.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous upsert.</returns>
    Task AddDocumentAsync(WorkspaceDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a document membership from a workspace (the document remains in the vector store).
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete.</returns>
    Task RemoveDocumentAsync(string workspaceId, string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all document memberships for a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The workspace's document memberships.</returns>
    Task<IReadOnlyList<WorkspaceDocument>> ListDocumentsAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an already-ingested document by its content hash for embed-once deduplication.
    /// </summary>
    /// <param name="contentHash">The SHA-256 hex hash of the document content.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The existing document identifier, or null when no document has this hash.</returns>
    Task<string?> FindDocumentIdByHashAsync(string contentHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pins or unpins a document within a workspace (pinned documents are always in context).
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="pinned">True to pin, false to unpin.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous update.</returns>
    Task SetPinnedAsync(
        string workspaceId,
        string documentId,
        bool pinned,
        CancellationToken cancellationToken = default);
}
