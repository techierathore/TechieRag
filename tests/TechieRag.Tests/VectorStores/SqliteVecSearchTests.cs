using TechieRag.Models;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.VectorStores;

/// <summary>
/// <see cref="SqliteVecStore"/>'s vectorised managed search (REQ-RAG-056 / BRD-93): identical
/// ranking to the old path, and the edge cases the old path defined.
/// </summary>
public class SqliteVecSearchTests
{
    /// <summary>
    /// Over 2,000 random 384-dimension chunks and 25 queries, the new search returns the same ids in
    /// the same order as the old full scan, with scores equal to within float rounding.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-056 ManagedSearchRanksLikeTheOldPath")]
    public async Task ManagedSearchRanksLikeTheOldPath()
    {
        using var corpus = await SqliteSearchCorpus.CreateAsync(2_000, 384);
        var random = new Random(7);

        for (var q = 0; q < 25; q++)
        {
            var query = SqliteSearchCorpus.RandomVector(random, 384);

            var expected = await LegacySqliteSimilaritySearch.SearchAsync(corpus.ConnectionString, query, 10);
            var actual = await corpus.Store.SearchAsync(query, 10);

            Assert.Equal(expected.Select(e => e.Id), actual.Select(a => a.Chunk.Id));
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Score, actual[i].Score, 5);
            }
        }
    }

    /// <summary>The document filter restricts the scan exactly as before.</summary>
    [Fact]
    public async Task DocumentFilterRanksLikeTheOldPath()
    {
        using var corpus = await SqliteSearchCorpus.CreateAsync(1_000, 64);
        var query = SqliteSearchCorpus.RandomVector(new Random(3), 64);

        var expected = await LegacySqliteSimilaritySearch.SearchAsync(corpus.ConnectionString, query, 5, "doc-3");
        var actual = await corpus.Store.SearchAsync(query, 5, "doc-3");

        Assert.Equal(expected.Select(e => e.Id), actual.Select(a => a.Chunk.Id));
        Assert.All(actual, r => Assert.Equal("doc-3", r.Chunk.DocumentId));
    }

    /// <summary>
    /// Equal scores keep scan order (the stable sort's behaviour), a wrong-width vector scores 0, and
    /// the winners come back with their text, metadata and vector.
    /// </summary>
    [Fact]
    public async Task TiesAndMismatchesBehaveAsBefore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"techierag-ties-{Guid.NewGuid():N}.db");
        var store = new SqliteVecStore($"Data Source={path};Pooling=False", 3);
        try
        {
            await store.UpsertBatchAsync(
            [
                Chunk("a", [1, 0, 0]),
                Chunk("b", [0, 1, 0]),
                Chunk("c", [1, 0, 0]),
                Chunk("d", [1, 0]),
                Chunk("e", [0, 0, 0])
            ]);

            var results = await store.SearchAsync([1, 0, 0], topK: 5);

            Assert.Equal(["a", "c", "b", "d", "e"], results.Select(r => r.Chunk.Id));
            Assert.Equal(1f, results[0].Score, 5);
            Assert.Equal(0f, results[3].Score);
            Assert.Equal("text a", results[0].Chunk.Text);
            Assert.Equal("Doc", results[0].Chunk.Metadata["DocumentName"]?.ToString());
            Assert.Equal([1f, 0f, 0f], results[0].Chunk.Vector);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    /// <summary>A non-positive top-K and an empty store both return nothing.</summary>
    [Fact]
    public async Task EmptyAndZeroTopKReturnNothing()
    {
        using var corpus = await SqliteSearchCorpus.CreateAsync(0, 8);

        Assert.Empty(await corpus.Store.SearchAsync(new float[8], 5));
        Assert.Empty(await corpus.Store.SearchAsync(new float[8], 0));
    }

    /// <summary>The SIMD cosine agrees with the scalar definition on odd widths (vector tail).</summary>
    [Fact]
    public void SimdCosineMatchesScalar()
    {
        var random = new Random(11);
        foreach (var width in new[] { 1, 7, 17, 384, 1023, 1024 })
        {
            var a = SqliteSearchCorpus.RandomVector(random, width);
            var b = SqliteSearchCorpus.RandomVector(random, width);
            double dot = 0, na = 0, nb = 0;
            for (var i = 0; i < width; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }

            var expected = dot / (Math.Sqrt(na) * Math.Sqrt(nb));
            var actual = ManagedVectorSearch.CosineSimilarity(a, ManagedVectorSearch.Magnitude(a), b);

            Assert.Equal(expected, actual, 4);
        }
    }

    private static TextChunk Chunk(string id, float[] vector) => new()
    {
        Id = id,
        DocumentId = "doc",
        Text = $"text {id}",
        Vector = vector,
        Metadata = new Dictionary<string, object> { ["DocumentName"] = "Doc", ["SourcePath"] = "/doc" }
    };
}
