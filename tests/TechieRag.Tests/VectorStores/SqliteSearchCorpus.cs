using TechieRag.Models;
using TechieRag.VectorStores;

namespace TechieRag.Tests.VectorStores;

/// <summary>
/// Builds a temporary SQLite store filled with seeded random chunks, for the search tests and the
/// search benchmark (REQ-RAG-056).
/// </summary>
internal sealed class SqliteSearchCorpus : IDisposable
{
    private readonly string path;

    private SqliteSearchCorpus(string path, SqliteVecStore store, int dimensions)
    {
        this.path = path;
        Store = store;
        Dimensions = dimensions;
    }

    /// <summary>The filled store.</summary>
    public SqliteVecStore Store { get; }

    /// <summary>Its connection string.</summary>
    public string ConnectionString => $"Data Source={path};Pooling=False";

    /// <summary>The vector width.</summary>
    public int Dimensions { get; }

    /// <summary>Creates and fills a store.</summary>
    /// <param name="chunkCount">How many chunks.</param>
    /// <param name="dimensions">Vector width.</param>
    /// <param name="seed">Random seed, so every run stores the same vectors.</param>
    /// <param name="documents">How many documents the chunks spread over.</param>
    /// <returns>The corpus; dispose it to delete the file.</returns>
    public static async Task<SqliteSearchCorpus> CreateAsync(int chunkCount, int dimensions, int seed = 42, int documents = 10)
    {
        var path = Path.Combine(Path.GetTempPath(), $"techierag-search-{Guid.NewGuid():N}.db");
        var store = new SqliteVecStore($"Data Source={path};Pooling=False", dimensions);
        var random = new Random(seed);
        const int batchSize = 2_000;

        for (var start = 0; start < chunkCount; start += batchSize)
        {
            var batch = new List<TextChunk>(batchSize);
            for (var i = start; i < Math.Min(chunkCount, start + batchSize); i++)
            {
                batch.Add(new TextChunk
                {
                    Id = $"chunk-{i:D6}",
                    DocumentId = $"doc-{i % documents}",
                    Text = $"Chunk {i}: " + new string('x', 400),
                    Vector = RandomVector(random, dimensions),
                    ChunkIndex = i,
                    Metadata = new Dictionary<string, object>
                    {
                        ["DocumentName"] = $"Document {i % documents}",
                        ["SourcePath"] = $"/docs/{i % documents}.md",
                        ["Heading"] = $"Section {i % 17}"
                    }
                });
            }

            await store.UpsertBatchAsync(batch);
        }

        return new SqliteSearchCorpus(path, store, dimensions);
    }

    /// <summary>A seeded random vector with components in [-1, 1).</summary>
    /// <param name="random">The generator.</param>
    /// <param name="dimensions">Width.</param>
    /// <returns>The vector.</returns>
    public static float[] RandomVector(Random random, int dimensions)
    {
        var vector = new float[dimensions];
        for (var d = 0; d < dimensions; d++)
        {
            vector[d] = (float)(random.NextDouble() * 2 - 1);
        }

        return vector;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A file still held open is left for the OS temp cleanup.
        }
    }
}
