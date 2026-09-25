using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Persistence;
using TechieRag.Tests.TestDoubles;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.Persistence;

/// <summary>
/// REQ-RAG-095 / BRD-142 (TR-RAG-004): deleting a document removes its vectors AND its membership
/// from every workspace, against a real SQLite vector store and a real SQLite workspace store.
/// </summary>
public sealed class DocumentDeletionTests : IDisposable
{
    private readonly string vectorPath = Path.Combine(Path.GetTempPath(), $"trdel-vec-{Guid.NewGuid():N}.db");
    private readonly string workspacePath = Path.Combine(Path.GetTempPath(), $"trdel-ws-{Guid.NewGuid():N}.db");
    private readonly SqliteVecStore vectors;
    private readonly SqliteWorkspaceStore workspaces;
    private readonly TechieRagClient client;

    /// <summary>Creates a client over two temp SQLite databases, one for vectors and one for workspaces.</summary>
    public DocumentDeletionTests()
    {
        vectors = new SqliteVecStore($"Data Source={vectorPath}", 3);
        workspaces = new SqliteWorkspaceStore($"Data Source={workspacePath}");
        client = new TechieRagClient(
            vectors,
            new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            new TechieRagConfig(),
            NullLogger<TechieRagClient>.Instance,
            workspaceStore: workspaces);
    }

    /// <summary>
    /// A document in two workspaces is deleted through the client: its chunks leave the vector
    /// store, and neither workspace lists it any more.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-095 DeletingADocumentRemovesItsVectorsAndEveryMembership")]
    public async Task DeletingADocumentRemovesItsVectorsAndEveryMembership()
    {
        await client.InitializeAsync();
        var doomed = await client.IngestTextAsync("the contract renews every March", "contract.txt");
        var (first, second) = await TwoWorkspacesHoldingAsync(doomed);

        await client.DeleteDocumentAsync(doomed);

        Assert.DoesNotContain(await vectors.ListDocumentsAsync(), document => document.Id == doomed);
        Assert.DoesNotContain(
            await vectors.SearchAsync([0.1f, 0.2f, 0.3f], topK: 10),
            result => result.Chunk.DocumentId == doomed);
        Assert.Empty(await workspaces.ListDocumentsAsync(first));
        Assert.Empty(await workspaces.ListDocumentsAsync(second));
    }

    /// <summary>
    /// Deleting one document leaves another document's vectors and memberships alone, so the
    /// cleanup is scoped to the document named and not to the workspaces it was in.
    /// </summary>
    [Fact]
    public async Task DeletingADocumentLeavesOtherDocumentsAlone()
    {
        await client.InitializeAsync();
        var doomed = await client.IngestTextAsync("the contract renews every March", "contract.txt");
        var kept = await client.IngestTextAsync("the office opens at nine", "hours.txt");
        var (first, _) = await TwoWorkspacesHoldingAsync(doomed);
        await workspaces.AddDocumentAsync(new WorkspaceDocument { WorkspaceId = first, DocumentId = kept, ContentHash = "h-kept" });

        await client.DeleteDocumentAsync(doomed);

        Assert.Contains(await vectors.ListDocumentsAsync(), document => document.Id == kept);
        var remaining = Assert.Single(await workspaces.ListDocumentsAsync(first));
        Assert.Equal(kept, remaining.DocumentId);
    }

    /// <summary>
    /// A workspace store that does not override the new member still removes every membership,
    /// through the interface's default implementation over <see cref="IWorkspaceStore.RemoveDocumentAsync"/>.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-095 TheDefaultImplementationRemovesEveryMembership")]
    public async Task TheDefaultImplementationRemovesEveryMembership()
    {
        await workspaces.InitializeAsync();
        var (first, second) = await TwoWorkspacesHoldingAsync("doc-1");
        IWorkspaceStore wrapped = new DelegatingWorkspaceStore(workspaces);

        await wrapped.RemoveDocumentFromAllWorkspacesAsync("doc-1");

        Assert.Empty(await workspaces.ListDocumentsAsync(first));
        Assert.Empty(await workspaces.ListDocumentsAsync(second));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(vectorPath)) File.Delete(vectorPath);
        if (File.Exists(workspacePath)) File.Delete(workspacePath);
    }

    private async Task<(string First, string Second)> TwoWorkspacesHoldingAsync(string documentId)
    {
        var first = await workspaces.CreateWorkspaceAsync(new Workspace { Name = "Legal" });
        var second = await workspaces.CreateWorkspaceAsync(new Workspace { Name = "Finance" });

        foreach (var workspace in new[] { first, second })
        {
            await workspaces.AddDocumentAsync(new WorkspaceDocument
            {
                WorkspaceId = workspace.WorkspaceId,
                DocumentId = documentId,
                ContentHash = "h-" + documentId
            });
        }

        return (first.WorkspaceId, second.WorkspaceId);
    }

    /// <summary>
    /// An external-style implementation that forwards the original members only, so the interface's
    /// default <see cref="IWorkspaceStore.RemoveDocumentFromAllWorkspacesAsync"/> is what runs.
    /// </summary>
    /// <param name="inner">The store forwarded to.</param>
    private sealed class DelegatingWorkspaceStore(IWorkspaceStore inner) : IWorkspaceStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => inner.InitializeAsync(cancellationToken);

        public Task<Workspace> CreateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default) =>
            inner.CreateWorkspaceAsync(workspace, cancellationToken);

        public Task<Workspace?> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            inner.GetWorkspaceAsync(workspaceId, cancellationToken);

        public Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken cancellationToken = default) =>
            inner.ListWorkspacesAsync(cancellationToken);

        public Task UpdateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default) =>
            inner.UpdateWorkspaceAsync(workspace, cancellationToken);

        public Task DeleteWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            inner.DeleteWorkspaceAsync(workspaceId, cancellationToken);

        public Task AddDocumentAsync(WorkspaceDocument document, CancellationToken cancellationToken = default) =>
            inner.AddDocumentAsync(document, cancellationToken);

        public Task RemoveDocumentAsync(string workspaceId, string documentId, CancellationToken cancellationToken = default) =>
            inner.RemoveDocumentAsync(workspaceId, documentId, cancellationToken);

        public Task<IReadOnlyList<WorkspaceDocument>> ListDocumentsAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            inner.ListDocumentsAsync(workspaceId, cancellationToken);

        public Task<string?> FindDocumentIdByHashAsync(string contentHash, CancellationToken cancellationToken = default) =>
            inner.FindDocumentIdByHashAsync(contentHash, cancellationToken);

        public Task SetPinnedAsync(string workspaceId, string documentId, bool pinned, CancellationToken cancellationToken = default) =>
            inner.SetPinnedAsync(workspaceId, documentId, pinned, cancellationToken);
    }
}
