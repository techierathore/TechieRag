using TechieRag.Models;
using TechieRag.Persistence;
using Xunit;

namespace TechieRag.Tests.Persistence;

/// <summary>
/// End-to-end tests for the workspace/collection persistence primitives (REQ-RAG-028) using a
/// real temporary SQLite database. Verifies workspace CRUD with per-workspace settings,
/// document membership, content-hash deduplication lookup, and document pinning.
/// </summary>
public class SqliteWorkspaceStoreTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"trws-{Guid.NewGuid():N}.db");
    private readonly SqliteWorkspaceStore store;

    /// <summary>Creates a store over a unique temp database file.</summary>
    public SqliteWorkspaceStoreTests() => store = new SqliteWorkspaceStore($"Data Source={dbPath}");

    /// <summary>A workspace round-trips with all of its per-workspace retrieval/generation settings.</summary>
    [Fact]
    public async Task PersistsWorkspaceSettings()
    {
        var ws = new Workspace
        {
            Name = "Legal",
            SystemPrompt = "Answer as a lawyer",
            LlmModel = "gpt-4o",
            SimilarityThreshold = 0.42f,
            TopK = 8,
            RerankEnabled = true,
            ChatMode = WorkspaceChatMode.Query
        };

        await store.CreateWorkspaceAsync(ws);
        var loaded = await store.GetWorkspaceAsync(ws.WorkspaceId);

        Assert.NotNull(loaded);
        Assert.Equal("Legal", loaded!.Name);
        Assert.Equal("Answer as a lawyer", loaded.SystemPrompt);
        Assert.Equal("gpt-4o", loaded.LlmModel);
        Assert.Equal(0.42f, loaded.SimilarityThreshold);
        Assert.Equal(8, loaded.TopK);
        Assert.True(loaded.RerankEnabled);
        Assert.Equal(WorkspaceChatMode.Query, loaded.ChatMode);
    }

    /// <summary>Updating a workspace persists the new settings.</summary>
    [Fact]
    public async Task UpdatesWorkspace()
    {
        var ws = new Workspace { Name = "Original" };
        await store.CreateWorkspaceAsync(ws);

        ws.Name = "Renamed";
        ws.TopK = 3;
        await store.UpdateWorkspaceAsync(ws);

        var loaded = await store.GetWorkspaceAsync(ws.WorkspaceId);
        Assert.Equal("Renamed", loaded!.Name);
        Assert.Equal(3, loaded.TopK);
    }

    /// <summary>Documents can be added to and listed from a workspace, and pinning is honored.</summary>
    [Fact]
    public async Task AddsListsAndPinsDocuments()
    {
        var ws = new Workspace { Name = "Docs" };
        await store.CreateWorkspaceAsync(ws);

        await store.AddDocumentAsync(new WorkspaceDocument { WorkspaceId = ws.WorkspaceId, DocumentId = "d1", ContentHash = "h1" });
        await store.AddDocumentAsync(new WorkspaceDocument { WorkspaceId = ws.WorkspaceId, DocumentId = "d2", ContentHash = "h2", IsPinned = true });

        var docs = await store.ListDocumentsAsync(ws.WorkspaceId);
        Assert.Equal(2, docs.Count);
        Assert.Contains(docs, d => d.DocumentId == "d2" && d.IsPinned);

        await store.SetPinnedAsync(ws.WorkspaceId, "d1", pinned: true);
        var afterPin = await store.ListDocumentsAsync(ws.WorkspaceId);
        Assert.All(afterPin, d => Assert.True(d.IsPinned));
    }

    /// <summary>Re-adding an existing membership upserts (no duplicate primary key error).</summary>
    [Fact]
    public async Task ReAddingDocumentUpserts()
    {
        var ws = new Workspace { Name = "Upsert" };
        await store.CreateWorkspaceAsync(ws);

        await store.AddDocumentAsync(new WorkspaceDocument { WorkspaceId = ws.WorkspaceId, DocumentId = "d1", ContentHash = "h1" });
        await store.AddDocumentAsync(new WorkspaceDocument { WorkspaceId = ws.WorkspaceId, DocumentId = "d1", ContentHash = "h1", IsPinned = true });

        var docs = await store.ListDocumentsAsync(ws.WorkspaceId);
        Assert.Single(docs);
        Assert.True(docs[0].IsPinned);
    }

    /// <summary>Content-hash lookup returns the document id for embed-once deduplication.</summary>
    [Fact]
    public async Task FindsDocumentIdByContentHash()
    {
        var ws = new Workspace { Name = "Dedup" };
        await store.CreateWorkspaceAsync(ws);
        await store.AddDocumentAsync(new WorkspaceDocument { WorkspaceId = ws.WorkspaceId, DocumentId = "doc-99", ContentHash = "abc123" });

        Assert.Equal("doc-99", await store.FindDocumentIdByHashAsync("abc123"));
        Assert.Null(await store.FindDocumentIdByHashAsync("nope"));
    }

    /// <summary>Deleting a workspace removes it and its memberships.</summary>
    [Fact]
    public async Task DeletesWorkspaceAndMemberships()
    {
        var ws = new Workspace { Name = "Temp" };
        await store.CreateWorkspaceAsync(ws);
        await store.AddDocumentAsync(new WorkspaceDocument { WorkspaceId = ws.WorkspaceId, DocumentId = "d1", ContentHash = "h1" });

        await store.DeleteWorkspaceAsync(ws.WorkspaceId);

        Assert.Null(await store.GetWorkspaceAsync(ws.WorkspaceId));
        Assert.Empty(await store.ListDocumentsAsync(ws.WorkspaceId));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }
}
