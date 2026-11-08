using Npgsql;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Persistence;
using Xunit;

namespace TechieRag.Tests.VectorStores.Live;

/// <summary>
/// The PostgreSQL path end to end on a real PostgreSQL 18 with pgvector (REQ-RAG-152 / BRD-210):
/// vectors, conversations and workspaces in one database through one <c>UsePostgres</c> call.
/// </summary>
/// <remarks>
/// <para>Runs against the server <c>tests/TechieRag.Tests/livetests/start-postgres.sh</c> starts in Docker,
/// reached through the connection string that script writes to <c>livetests.settings.json</c>; skips with
/// the reason when the file is absent (<see cref="LivePostgresFactAttribute"/>).</para>
/// <para>Every test works in its own id namespace and removes what it wrote. The vector size is 1024, the
/// same as <see cref="LivePgVectorStoreTests"/>, because both share the <c>Chunks</c> table of one database.</para>
/// </remarks>
[Trait("Category", LivePostgresFactAttribute.CategoryName)]
public sealed class LivePostgresEndToEndTests
{
    private const int Dimension = 1024;

    /// <summary>
    /// One <c>UsePostgres(connectionString)</c> call round-trips a document's vectors, a conversation and a
    /// workspace, and all their tables are in the one database the connection string names.
    /// </summary>
    [LivePostgresFact(DisplayName = "REQ-RAG-152 UsePostgresRoundTripsVectorsConversationsAndWorkspacesInOneDatabase")]
    public async Task UsePostgresRoundTripsInOneDatabase()
    {
        var connectionString = LivePostgresFactAttribute.ConnectionString!;
        var user = $"techierag-test-{Guid.NewGuid():N}";
        var rag = new TechieRagBuilder()
            .UsePostgres(connectionString, defaultUserId: user)
            .UseCustomEmbeddingProvider(() => new OneHotEmbeddingProvider(Dimension))
            .Build();
        await rag.InitializeAsync();

        var documentId = await rag.IngestTextAsync("the harbour opens at seven", $"techierag-test-{Guid.NewGuid():N}.txt");
        var workspaceManager = rag.GetWorkspaceManager();
        var conversations = rag.GetConversationStore();
        Workspace? workspace = null;

        try
        {
            var hits = await rag.SearchAsync("the harbour opens at seven", topK: 5, documentFilter: documentId);
            Assert.Contains(hits, hit => hit.Chunk.Text.Contains("harbour", StringComparison.Ordinal));

            Assert.NotNull(conversations);
            var thread = await conversations.CreateThreadAsync(user, title: "end to end");
            await conversations.AddMessageAsync(thread.ThreadId, ChatMessage.User("when does the harbour open?"));
            await conversations.AddMessageAsync(thread.ThreadId, ChatMessage.Assistant("at seven"));
            var messages = await conversations.GetMessagesAsync(thread.ThreadId);
            Assert.Equal(["when does the harbour open?", "at seven"], messages.Select(m => m.Content));

            Assert.NotNull(workspaceManager);
            workspace = await workspaceManager.CreateWorkspaceAsync($"techierag-test-{Guid.NewGuid():N}", w => w.TopK = 3);
            var readBack = await workspaceManager.GetWorkspaceAsync(workspace.WorkspaceId);
            Assert.NotNull(readBack);
            Assert.Equal(workspace.Name, readBack.Name);
            Assert.Equal(3, readBack.TopK);

            var tables = await ListTablesAsync(connectionString);
            Assert.Contains("chunks", tables);
            Assert.Contains("trthread", tables);
            Assert.Contains("trmessage", tables);
            Assert.Contains("trworkspace", tables);
            Assert.Contains("trworkspacedocument", tables);
        }
        finally
        {
            await rag.DeleteDocumentAsync(documentId);
            if (conversations is not null)
            {
                await conversations.DeleteAllForUserAsync(user);
            }

            if (workspace is not null && workspaceManager is not null)
            {
                await workspaceManager.DeleteWorkspaceAsync(workspace.WorkspaceId);
            }
        }
    }

    /// <summary>A conversation written to a real server reads back in order, with its title, and deletes cleanly.</summary>
    [LivePostgresFact(DisplayName = "REQ-RAG-152 PostgresConversationStoreRoundTripsAThread")]
    public async Task PostgresConversationStoreRoundTripsAThread()
    {
        var store = new PostgresConversationStore(LivePostgresFactAttribute.ConnectionString!);
        await store.InitializeAsync();
        var user = $"techierag-test-{Guid.NewGuid():N}";

        try
        {
            var thread = await store.CreateThreadAsync(user, title: "first");
            await store.RenameThreadAsync(thread.ThreadId, "renamed");
            await store.AddMessageAsync(thread.ThreadId, ChatMessage.User("one"));
            await store.AddMessageAsync(thread.ThreadId, ChatMessage.Assistant("two"));

            var readBack = await store.GetThreadAsync(thread.ThreadId);
            Assert.NotNull(readBack);
            Assert.Equal("renamed", readBack.Title);
            Assert.Equal(["one", "two"], (await store.GetMessagesAsync(thread.ThreadId)).Select(m => m.Content));
            Assert.Single(await store.ListThreadsAsync(user));

            await store.DeleteThreadAsync(thread.ThreadId);
            Assert.Null(await store.GetThreadAsync(thread.ThreadId));
        }
        finally
        {
            await store.DeleteAllForUserAsync(user);
        }
    }

    /// <summary>A workspace and its document membership written to a real server read back, pin and delete.</summary>
    [LivePostgresFact(DisplayName = "REQ-RAG-152 PostgresWorkspaceStoreRoundTripsAWorkspaceAndItsDocuments")]
    public async Task PostgresWorkspaceStoreRoundTripsAWorkspace()
    {
        var store = new PostgresWorkspaceStore(LivePostgresFactAttribute.ConnectionString!);
        await store.InitializeAsync();
        var workspace = await store.CreateWorkspaceAsync(new Workspace { Name = $"techierag-test-{Guid.NewGuid():N}", SystemPrompt = "be brief" });
        var documentId = $"techierag-test-{Guid.NewGuid():N}";
        var hash = Guid.NewGuid().ToString("N");

        try
        {
            await store.AddDocumentAsync(new WorkspaceDocument { WorkspaceId = workspace.WorkspaceId, DocumentId = documentId, ContentHash = hash });
            await store.SetPinnedAsync(workspace.WorkspaceId, documentId, true);

            var readBack = await store.GetWorkspaceAsync(workspace.WorkspaceId);
            Assert.NotNull(readBack);
            Assert.Equal("be brief", readBack.SystemPrompt);
            var document = Assert.Single(await store.ListDocumentsAsync(workspace.WorkspaceId));
            Assert.Equal(documentId, document.DocumentId);
            Assert.True(document.IsPinned);
            Assert.Equal(documentId, await store.FindDocumentIdByHashAsync(hash));

            await store.RemoveDocumentAsync(workspace.WorkspaceId, documentId);
            Assert.Empty(await store.ListDocumentsAsync(workspace.WorkspaceId));
        }
        finally
        {
            await store.DeleteWorkspaceAsync(workspace.WorkspaceId);
        }

        Assert.Null(await store.GetWorkspaceAsync(workspace.WorkspaceId));
    }

    /// <summary>Lists the table names of the connection string's database, in its public schema.</summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The table names, lower case as PostgreSQL folds unquoted names.</returns>
    private static async Task<HashSet<string>> ListTablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_catalog = current_database() AND table_schema = 'public'",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>An embedding provider whose vector for a text is one-hot at the text's stable hash.</summary>
    /// <param name="dimensions">The vector size.</param>
    private sealed class OneHotEmbeddingProvider(int dimensions) : IEmbeddingProvider
    {
        /// <inheritdoc/>
        public string Name => "OneHot";

        /// <inheritdoc/>
        public string ModelName => "one-hot";

        /// <inheritdoc/>
        public int Dimensions => dimensions;

#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
        /// <inheritdoc/>
        public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;
#pragma warning restore CS0067

        /// <inheritdoc/>
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(Embed(text));

        /// <inheritdoc/>
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());

        /// <summary>Builds the one-hot vector for a text.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The vector.</returns>
        private float[] Embed(string text)
        {
            var vector = new float[dimensions];
            var hash = 17u;
            foreach (var character in text.Trim())
            {
                hash = unchecked((hash * 31) + character);
            }

            vector[hash % (uint)dimensions] = 1f;
            return vector;
        }
    }
}
