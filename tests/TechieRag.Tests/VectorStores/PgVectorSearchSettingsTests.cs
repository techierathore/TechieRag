using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.VectorStores;

/// <summary>
/// REQ-RAG-161 (Sevak TR-RAG-060), without a server: the vector index and the per-search settings that make
/// an index scan return as many rows as asked. The live proof is <c>LivePgVectorIndexTests</c>.
/// </summary>
public sealed class PgVectorSearchSettingsTests
{
    /// <summary>The vector index is HNSW, which needs no training rows, never IVFFlat.</summary>
    [Fact(DisplayName = "REQ-RAG-161 VectorIndexIsHnsw")]
    public void VectorIndexIsHnsw()
    {
        Assert.Contains("USING hnsw (Embedding vector_cosine_ops)", PgVectorStore.VectorIndexSql, StringComparison.Ordinal);
        Assert.DoesNotContain("ivfflat", PgVectorStore.VectorIndexSql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The candidate list is widened to topK, never below HNSW's default 40 and never above pgvector's 1,000.
    /// </summary>
    /// <param name="topK">The search's topK.</param>
    /// <param name="expected">The ef_search value it runs with.</param>
    [Theory(DisplayName = "REQ-RAG-161 EfSearchFollowsTopK")]
    [InlineData(2, 40)]
    [InlineData(50, 50)]
    [InlineData(1000, 1000)]
    [InlineData(5000, 1000)]
    public void EfSearchFollowsTopK(int topK, int expected)
    {
        Assert.StartsWith($"SET LOCAL hnsw.ef_search = {expected}", PgVectorStore.SearchSettingsSql(topK, iterativeScan: true, filtered: false), StringComparison.Ordinal);
    }

    /// <summary>On pgvector 0.8.0 or later every search turns on the strict-order iterative scan.</summary>
    [Fact(DisplayName = "REQ-RAG-161 IterativeScanOnWhenSupported")]
    public void IterativeScanOnWhenSupported()
    {
        var sql = PgVectorStore.SearchSettingsSql(5, iterativeScan: true, filtered: true);
        Assert.Contains("hnsw.iterative_scan = strict_order", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("enable_indexscan", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// On an older pgvector a filtered search, or one for more than 1,000 results, is answered exactly; a plain
    /// small search keeps the index.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-161 OlderPgvectorFallsBackToExact")]
    public void OlderPgvectorFallsBackToExact()
    {
        Assert.Contains("enable_indexscan = off", PgVectorStore.SearchSettingsSql(5, iterativeScan: false, filtered: true), StringComparison.Ordinal);
        Assert.Contains("enable_indexscan = off", PgVectorStore.SearchSettingsSql(1500, iterativeScan: false, filtered: false), StringComparison.Ordinal);
        Assert.DoesNotContain("enable_indexscan", PgVectorStore.SearchSettingsSql(5, iterativeScan: false, filtered: false), StringComparison.Ordinal);
    }

    /// <summary>The iterative scan is used from pgvector 0.8.0 on, and never on an unreadable version.</summary>
    /// <param name="version">The extension version text.</param>
    /// <param name="expected">Whether the iterative scan is used.</param>
    [Theory(DisplayName = "REQ-RAG-161 IterativeScanFromVersionEight")]
    [InlineData("0.8.0", true)]
    [InlineData("0.8.6", true)]
    [InlineData("1.0", true)]
    [InlineData("0.7.4", false)]
    [InlineData("0.5.1", false)]
    [InlineData(null, false)]
    [InlineData("dev", false)]
    public void IterativeScanFromVersionEight(string? version, bool expected)
    {
        Assert.Equal(expected, PgVectorStore.SupportsIterativeScan(version));
    }
}
