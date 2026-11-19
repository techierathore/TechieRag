using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Tests.TestDoubles;
using TechieRag.Tests.VectorStores.Live;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.VectorStores;

/// <summary>
/// REQ-RAG-169 (TrStudio TR-RAG-003): a search can be limited to chunks whose metadata carries given values
/// (an owner, a character), applied inside the vector store.
/// </summary>
/// <remarks>
/// <para><b>The report.</b> On 1.1.5 <c>SearchAsync</c> could filter by document id only, so TrStudio over-fetched
/// and filtered by owner and character in memory. Over-fetching misses: when another owner's chunks are closer to
/// the query, a small owner's chunks fall outside any fixed over-fetch and the search finds nothing for them.</para>
/// <para><b>The case every test builds.</b> Forty chunks of owner <c>a</c> about Saturn, two of owner <c>b</c> about
/// Saturn and Mars, and a query about Saturn: owner <c>a</c>'s chunks are all closer, so the best 2, or even the best
/// 10 filtered afterwards, hold none of owner <c>b</c>'s.</para>
/// </remarks>
public sealed class MetadataFilterTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "trmeta-" + Guid.NewGuid().ToString("N"));

    /// <summary>Initializes a new instance of the <see cref="MetadataFilterTests"/> class.</summary>
    public MetadataFilterTests() => Directory.CreateDirectory(folder);

    /// <summary>
    /// Through the client on the SQLite store: filtered to owner <c>b</c>, the search returns exactly owner
    /// <c>b</c>'s two chunks; filtered after the fact, a ten-result over-fetch holds none of them.
    /// </summary>
    /// <returns>A task that completes when the searches have run.</returns>
    [Fact(DisplayName = "REQ-RAG-169 SqliteFilterFindsTheSmallOwner")]
    public async Task SqliteFilterFindsTheSmallOwner()
    {
        var rag = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new KeywordEmbedder())
            .UseSqliteVec(Path.Combine(folder, "rag.db"))
            .WithChunkSize(2000, 0)
            .Build();
        await rag.InitializeAsync();
        for (var i = 0; i < 40; i++)
        {
            await rag.IngestTextAsync($"Saturn note {i}", $"a-{i}", new Dictionary<string, object> { ["ownerId"] = "a", ["characterId"] = "c1" });
        }

        await rag.IngestTextAsync("Saturn and Mars, first", "b-1", new Dictionary<string, object> { ["ownerId"] = "b", ["characterId"] = "c2" });
        await rag.IngestTextAsync("Saturn and Mars, second", "b-2", new Dictionary<string, object> { ["ownerId"] = "b", ["characterId"] = "c3" });

        var overFetched = await rag.SearchAsync("Saturn", topK: 10);
        var filtered = await rag.SearchAsync("Saturn", new SearchOptions { TopK = 2, MetadataFilter = new Dictionary<string, string> { ["ownerId"] = "b" } });
        var narrower = await rag.SearchAsync("Saturn", new SearchOptions { TopK = 5, MetadataFilter = new Dictionary<string, string> { ["ownerId"] = "b", ["characterId"] = "c3" } });

        Assert.DoesNotContain(overFetched, result => MetadataMatch.TextOf(result.Chunk.Metadata["ownerId"]) == "b");
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, result => Assert.Equal("b", MetadataMatch.TextOf(result.Chunk.Metadata["ownerId"])));
        Assert.Equal("Saturn and Mars, second", Assert.Single(narrower).Chunk.Text);
    }

    /// <summary>
    /// A store without a native filter (Qdrant, a third-party store) takes the default, which widens its candidate
    /// set until it has enough matches, so it returns owner <c>b</c>'s chunks too.
    /// </summary>
    /// <returns>A task that completes when the search has run.</returns>
    [Fact(DisplayName = "REQ-RAG-169 DefaultFilterWidensUntilItFindsTheSmallOwner")]
    public async Task DefaultFilterWidensUntilItFindsTheSmallOwner()
    {
        var ranked = Enumerable.Range(0, 40).Select(i => Result($"a-{i}", "a", 0.9f - (i * 0.001f)))
            .Append(Result("b-1", "b", 0.5f))
            .Append(Result("b-2", "b", 0.4f))
            .ToList();
        IVectorStore store = new FakeVectorStore(ranked);

        var matched = await store.SearchMatchingAsync([1f], 2, null, new Dictionary<string, string> { ["ownerId"] = "b" });

        Assert.Equal(["b-1", "b-2"], matched.Select(result => result.Chunk.DocumentId));
    }

    /// <summary>A key no store can apply the same way is refused before any search, naming the key.</summary>
    /// <returns>A task that completes when the call has failed.</returns>
    [Fact(DisplayName = "REQ-RAG-169 UnusableKeyIsRefused")]
    public async Task UnusableKeyIsRefused()
    {
        IVectorStore store = new SqliteVecStore("Data Source=" + Path.Combine(folder, "keys.db"), 3);

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => store.SearchMatchingAsync([1f, 0f, 0f], 2, null, new Dictionary<string, string> { ["owner\"Id"] = "b" }));

        Assert.Contains("owner\"Id", error.Message);
    }

    /// <summary>
    /// On a real PostgreSQL with pgvector, the filter is part of the query: owner <c>b</c>'s two chunks come back
    /// while the planner is held to the vector index, and a document scope narrows it further.
    /// </summary>
    /// <returns>A task that completes when the searches have run.</returns>
    [LivePostgresFact(DisplayName = "REQ-RAG-169 PgVectorFiltersInsideTheQuery")]
    [Trait("Category", LivePostgresFactAttribute.CategoryName)]
    public async Task PgVectorFiltersInsideTheQuery()
    {
        var schema = "trtest" + Guid.NewGuid().ToString("N")[..12];
        await ExecuteAsync($"CREATE SCHEMA {schema}");
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(LivePostgresFactAttribute.ConnectionString!)
            {
                SearchPath = schema + ",public",
                Pooling = false,
                Options = "-c enable_seqscan=off"
            };
            await using var store = new PgVectorStore(connection.ConnectionString, NullLogger<PgVectorStore>.Instance, 3);
            await store.InitializeAsync();
            var embedder = new KeywordEmbedder();
            var chunks = Enumerable.Range(0, 40)
                .Select(i => Chunk($"a-{i}", $"Saturn note {i}", "a", embedder))
                .Append(Chunk("b-1", "Saturn and Mars, first", "b", embedder))
                .Append(Chunk("b-2", "Saturn and Mars, second", "b", embedder))
                .ToList();
            await store.UpsertBatchAsync(chunks);
            var query = await embedder.EmbedAsync("Saturn");

            var filtered = await store.SearchMatchingAsync(query, 2, null, new Dictionary<string, string> { ["ownerId"] = "b" });
            var scoped = await store.SearchMatchingAsync(query, 5, ["b-2"], new Dictionary<string, string> { ["ownerId"] = "b" });

            Assert.Equal(["b-1", "b-2"], filtered.Select(result => result.Chunk.DocumentId).Order());
            Assert.Equal("b-2", Assert.Single(scoped).Chunk.DocumentId);
        }
        finally
        {
            await ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still held is left in the temp folder.
        }
    }

    private static SearchResult Result(string documentId, string owner, float score) => new()
    {
        Chunk = new TextChunk { DocumentId = documentId, Text = documentId, Metadata = new Dictionary<string, object> { ["ownerId"] = owner } },
        Score = score
    };

    private static TextChunk Chunk(string documentId, string text, string owner, KeywordEmbedder embedder) => new()
    {
        DocumentId = documentId,
        Text = text,
        Vector = embedder.Vector(text),
        Metadata = new Dictionary<string, object> { ["ownerId"] = owner, ["DocumentName"] = documentId }
    };

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(LivePostgresFactAttribute.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A three-word embedder: Saturn, Mars and a constant, so "Saturn" is closer to a Saturn-only note.</summary>
    private sealed class KeywordEmbedder : IEmbeddingProvider
    {
        public string Name => "Keyword";

        public string ModelName => "keyword-3";

        public int Dimensions => 3;

#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
        public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;
#pragma warning restore CS0067

        public float[] Vector(string text)
        {
            var saturn = text.Contains("Saturn", StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
            var mars = text.Contains("Mars", StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
            return [saturn, mars, 0.1f];
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(Vector(text));

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Vector).ToList());
    }
}
