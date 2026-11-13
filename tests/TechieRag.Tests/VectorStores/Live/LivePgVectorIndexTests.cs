using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TechieRag.Models;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.VectorStores.Live;

/// <summary>
/// REQ-RAG-161 (Sevak TR-RAG-060): a <c>PgVectorStore</c> search returns every stored chunk whatever plan
/// PostgreSQL picks, against a real PostgreSQL with pgvector.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> <c>InitializeAsync</c> built an IVFFlat index on the empty <c>Chunks</c> table.
/// IVFFlat trains its list centres from the rows present when it is built, so every later row landed in
/// lists the default <c>ivfflat.probes = 1</c> never reads: a search over N chunks returned some, one or
/// none of them, depending on whether the planner chose the index.</para>
/// <para><b>Each test gets its own schema</b> (<c>Search Path=&lt;schema&gt;,public</c>), so its index is
/// built on an empty table exactly as on a new database, and a run never touches another run's tables.</para>
/// <para><b>The planner is pinned to the index</b> with <c>enable_seqscan=off</c> on the connection, so the
/// result does not depend on PostgreSQL's cost guess for a small table. That is the worst case for the old
/// index and the case a real corpus reaches once it is big enough for the index to win.</para>
/// </remarks>
[Trait("Category", LivePostgresFactAttribute.CategoryName)]
public sealed class LivePgVectorIndexTests
{
    private const int Dimension = 64;

    /// <summary>
    /// On a new database, 50 chunks stored after start-up all come back from a search with topK 50, ordered
    /// by cosine similarity, while the planner uses the vector index.
    /// </summary>
    /// <remarks>
    /// 50 is above HNSW's default candidate list of 40, so this also proves the search widens the list to
    /// topK rather than silently capping the result at 40.
    /// </remarks>
    [LivePostgresFact(DisplayName = "REQ-RAG-161 SearchOnNewDatabaseReturnsEveryChunk")]
    public async Task SearchOnNewDatabaseReturnsEveryChunk()
    {
        await using var schema = await TestSchema.CreateAsync();
        await using var store = schema.NewStore(indexOnly: true);
        await store.InitializeAsync();

        var chunks = Enumerable.Range(0, 50)
            .Select(i => new TextChunk { DocumentId = "doc-" + (i % 5), Text = "chunk " + i, Vector = SpreadVector(i) })
            .ToList();
        await store.UpsertBatchAsync(chunks);

        var query = SpreadVector(7);
        var results = await store.SearchAsync(query, topK: 50);

        Assert.Equal(50, results.Count);
        Assert.Equal(50, results.Select(r => r.Chunk.Id).Distinct().Count());
        Assert.Equal("chunk 7", results[0].Chunk.Text);
        Assert.True(
            results.Zip(results.Skip(1)).All(pair => pair.First.Score >= pair.Second.Score - 1e-5f),
            "results are not ordered by similarity");
    }

    /// <summary>
    /// The entry's own repro: two short texts stored on a new database, a search for two, run five times,
    /// returns both every time.
    /// </summary>
    [LivePostgresFact(DisplayName = "REQ-RAG-161 TwoChunksComeBackEveryRun")]
    public async Task TwoChunksComeBackEveryRun()
    {
        await using var schema = await TestSchema.CreateAsync();
        await using var store = schema.NewStore(indexOnly: true);
        await store.InitializeAsync();

        await store.UpsertBatchAsync(
        [
            new TextChunk { DocumentId = "doc-a", Text = "the ledger closes on Friday", Vector = SpreadVector(1) },
            new TextChunk { DocumentId = "doc-b", Text = "the van is serviced in March", Vector = SpreadVector(2) }
        ]);

        for (var run = 0; run < 5; run++)
        {
            Assert.Equal(2, (await store.SearchAsync(SpreadVector(3), topK: 2)).Count);
        }
    }

    /// <summary>
    /// A search filtered to one document returns all of that document's chunks even when every one of them
    /// is farther from the query than 40 chunks of other documents.
    /// </summary>
    /// <remarks>
    /// An approximate index reads its nearest candidates first and filters after; without the iterative
    /// scan the filter would leave nothing.
    /// </remarks>
    [LivePostgresFact(DisplayName = "REQ-RAG-161 FilteredSearchFindsFarDocument")]
    public async Task FilteredSearchFindsFarDocument()
    {
        await using var schema = await TestSchema.CreateAsync();
        await using var store = schema.NewStore(indexOnly: true);
        await store.InitializeAsync();

        var query = OneHot(0);
        var near = Enumerable.Range(0, 60)
            .Select(i => new TextChunk { DocumentId = "near", Text = "near " + i, Vector = Blend(OneHot(0), OneHot(1 + (i % 20)), 0.05f * (i % 7)) });
        var far = Enumerable.Range(0, 3)
            .Select(i => new TextChunk { DocumentId = "far", Text = "far " + i, Vector = OneHot(40 + i) });
        await store.UpsertBatchAsync(near.Concat(far));

        var results = await store.SearchAsync(query, topK: 3, documentFilter: "far");
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal("far", r.Chunk.DocumentId));

        var pinned = await store.SearchDocumentsAsync(query, 5, ["far"]);
        Assert.Equal(3, pinned.Count);
    }

    /// <summary>
    /// A database created by an earlier version, carrying the IVFFlat index, has it replaced on start-up and
    /// then returns every chunk.
    /// </summary>
    [LivePostgresFact(DisplayName = "REQ-RAG-161 EarlierIvfFlatIndexIsReplaced")]
    public async Task EarlierIvfFlatIndexIsReplaced()
    {
        await using var schema = await TestSchema.CreateAsync();
        await using (var setup = schema.NewStore(indexOnly: false))
        {
            await setup.InitializeAsync();
        }

        // What 1.1.9 and earlier left behind: the IVFFlat index, trained on the empty table.
        await schema.ExecuteAsync("DROP INDEX IF EXISTS IdxChunksEmbedding");
        await schema.ExecuteAsync("CREATE INDEX IdxChunksEmbedding ON Chunks USING ivfflat (Embedding vector_cosine_ops)");

        await using var store = schema.NewStore(indexOnly: true);
        await store.InitializeAsync();

        Assert.Equal("hnsw", await schema.IndexMethodAsync("idxchunksembedding"));

        await store.UpsertBatchAsync(Enumerable.Range(0, 20)
            .Select(i => new TextChunk { DocumentId = "doc", Text = "chunk " + i, Vector = SpreadVector(i) }));
        Assert.Equal(20, (await store.SearchAsync(SpreadVector(4), topK: 20)).Count);
    }

    /// <summary>A unit vector with a 1 in one component.</summary>
    /// <param name="index">The component.</param>
    /// <returns>The vector.</returns>
    private static float[] OneHot(int index)
    {
        var vector = new float[Dimension];
        vector[index % Dimension] = 1f;
        return vector;
    }

    /// <summary>A vector mostly along one axis and partly along another, so neighbours have distinct scores.</summary>
    /// <param name="seed">Which axes.</param>
    /// <returns>The vector.</returns>
    private static float[] SpreadVector(int seed) => Blend(OneHot(seed), OneHot(seed + 1), 0.3f);

    /// <summary>Adds <paramref name="weight"/> of <paramref name="other"/> to <paramref name="main"/>.</summary>
    /// <param name="main">The main direction.</param>
    /// <param name="other">The added direction.</param>
    /// <param name="weight">How much of it.</param>
    /// <returns>The blended vector.</returns>
    private static float[] Blend(float[] main, float[] other, float weight) =>
        main.Zip(other, (a, b) => a + (weight * b)).ToArray();

    /// <summary>A schema of its own on the live server, dropped with everything in it when disposed.</summary>
    private sealed class TestSchema : IAsyncDisposable
    {
        private readonly string name;

        private TestSchema(string name) => this.name = name;

        /// <summary>Creates a new empty schema.</summary>
        /// <returns>The schema.</returns>
        public static async Task<TestSchema> CreateAsync()
        {
            var schema = new TestSchema("trtest" + Guid.NewGuid().ToString("N")[..12]);
            await schema.ExecuteAsync($"CREATE SCHEMA {schema.name}");
            return schema;
        }

        /// <summary>A store whose tables live in this schema.</summary>
        /// <param name="indexOnly">Whether the planner is told not to scan the table directly.</param>
        /// <returns>The store; the caller initialises it.</returns>
        public PgVectorStore NewStore(bool indexOnly)
        {
            var builder = new NpgsqlConnectionStringBuilder(LivePostgresFactAttribute.ConnectionString!)
            {
                SearchPath = name + ",public",
                Pooling = false
            };
            if (indexOnly)
            {
                builder.Options = "-c enable_seqscan=off";
            }

            return new PgVectorStore(builder.ConnectionString, NullLogger<PgVectorStore>.Instance, Dimension);
        }

        /// <summary>Runs one statement inside this schema.</summary>
        /// <param name="sql">The statement.</param>
        /// <returns>A task.</returns>
        public async Task ExecuteAsync(string sql)
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Gets the access method of an index in this schema.</summary>
        /// <param name="indexName">The index name, lower case.</param>
        /// <returns>For example <c>hnsw</c>, or null when there is no such index.</returns>
        public async Task<string?> IndexMethodAsync(string indexName)
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT am.amname FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_am am ON am.oid = c.relam
                WHERE n.nspname = @Schema AND c.relname = @Index
                """;
            command.Parameters.AddWithValue("Schema", name);
            command.Parameters.AddWithValue("Index", indexName);
            return await command.ExecuteScalarAsync() as string;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync() => await ExecuteAsync($"DROP SCHEMA IF EXISTS {name} CASCADE");

        private async Task<NpgsqlConnection> OpenAsync()
        {
            var builder = new NpgsqlConnectionStringBuilder(LivePostgresFactAttribute.ConnectionString!)
            {
                SearchPath = name + ",public",
                Pooling = false
            };
            var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }
    }
}
