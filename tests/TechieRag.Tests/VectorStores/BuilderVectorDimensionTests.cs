using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.VectorStores;

/// <summary>
/// REQ-RAG-106 / BRD-158: <see cref="TechieRagBuilder.Build"/> sizes every vector store to the
/// embedding provider's <see cref="IEmbeddingProvider.Dimensions"/> instead of the 1024 default.
/// </summary>
/// <remarks>
/// No server is contacted: a store's width is fixed at construction and the pgvector DDL is
/// rendered from it, so both are readable from the built client without a database.
/// </remarks>
public sealed class BuilderVectorDimensionTests
{
    private const string PgConnectionString = "Host=localhost;Port=5432;Database=techierag;Username=nobody;Password=unused";

    /// <summary>
    /// Building with a 1536-dimension embedder and pgvector gives a store whose Chunks table is
    /// created as <c>vector(1536)</c>.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-106 PgVectorTakesTheEmbeddersDimensions")]
    public async Task PgVectorTakesTheEmbeddersDimensions()
    {
        var client = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new SizedEmbeddingProvider(1536))
            .UsePgVector(PgConnectionString)
            .Build();

        var store = Assert.IsType<PgVectorStore>(((TechieRagClient)client).VectorStore);
        await using (store)
        {
            Assert.Equal(1536, store.VectorDimension);
            Assert.Contains("Embedding vector(1536)", store.ChunksTableSql, StringComparison.Ordinal);
            Assert.DoesNotContain("vector(1024)", store.ChunksTableSql, StringComparison.Ordinal);
        }
    }

    /// <summary>A 3072-dimension embedder (Gemini's default) sizes a Qdrant collection to 3072.</summary>
    [Fact]
    public void QdrantTakesTheEmbeddersDimensions()
    {
        var client = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new SizedEmbeddingProvider(3072))
            .UseQdrant("http://localhost:6334")
            .Build();

        var store = Assert.IsType<QdrantStore>(((TechieRagClient)client).VectorStore);
        Assert.Equal(3072, store.Dimensions);
    }

    /// <summary>A 384-dimension embedder (MiniLM) sizes the SQLite store to 384.</summary>
    [Fact]
    public void SqliteTakesTheEmbeddersDimensions()
    {
        var client = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new SizedEmbeddingProvider(384))
            .UseSqliteVec(Path.Combine(Path.GetTempPath(), $"trdim-{Guid.NewGuid():N}.db"))
            .Build();

        var store = Assert.IsType<SqliteVecStore>(((TechieRagClient)client).VectorStore);
        Assert.Equal(384, store.Dimensions);
    }

    /// <summary>An embedder that reports no width leaves the store at the 1024 default rather than zero.</summary>
    [Fact]
    public async Task AnEmbedderReportingNoWidthKeepsTheDefault()
    {
        var client = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new SizedEmbeddingProvider(0))
            .UsePgVector(PgConnectionString)
            .Build();

        var store = Assert.IsType<PgVectorStore>(((TechieRagClient)client).VectorStore);
        await using (store)
        {
            Assert.Equal(1024, store.VectorDimension);
        }
    }

    /// <summary>An embedding provider that only reports a width; nothing here embeds.</summary>
    /// <param name="dimensions">The width it reports.</param>
    private sealed class SizedEmbeddingProvider(int dimensions) : IEmbeddingProvider
    {
        /// <inheritdoc/>
        public string Name => "Sized";

        /// <inheritdoc/>
        public string ModelName => $"sized-{dimensions}";

        /// <inheritdoc/>
        public int Dimensions => dimensions;

        /// <inheritdoc/>
#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
        public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;
#pragma warning restore CS0067

        /// <inheritdoc/>
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new float[dimensions]);

        /// <inheritdoc/>
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[dimensions]).ToList());
    }
}
