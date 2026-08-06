using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Reranking;

/// <summary>
/// Tests for the per-call rerank switch on <see cref="ITechieRag.SearchAsync(string, SearchOptions, CancellationToken)"/>
/// (REQ-RAG-047, TR-RAG-005). Reranking used to be decided solely by
/// <c>TechieRagConfig.Rerank.Enabled</c>; these tests prove a single call can now override it in
/// both directions while the legacy overload keeps its historical behaviour.
/// </summary>
public class SearchRerankSwitchTests
{
    /// <summary>
    /// A call that asks for reranking gets the reranked order even though the library-wide
    /// <c>Rerank.Enabled</c> flag is off — the switch is honored, not ignored.
    /// </summary>
    [Fact]
    public async Task PerCallRerankTrueOverridesGlobalDisabled()
    {
        var client = CreateClient(globalRerank: false);

        var results = await client.SearchAsync("q", new SearchOptions { TopK = 3, Rerank = true });

        Assert.Equal(["doc-c", "doc-b", "doc-a"], results.Select(r => r.Chunk.DocumentId));
    }

    /// <summary>
    /// A call that asks for no reranking keeps raw vector-similarity order even though the
    /// library-wide <c>Rerank.Enabled</c> flag is on.
    /// </summary>
    [Fact]
    public async Task PerCallRerankFalseOverridesGlobalEnabled()
    {
        var client = CreateClient(globalRerank: true);

        var results = await client.SearchAsync("q", new SearchOptions { TopK = 3, Rerank = false });

        Assert.Equal(["doc-a", "doc-b", "doc-c"], results.Select(r => r.Chunk.DocumentId));
    }

    /// <summary>
    /// Leaving the switch unset falls back to the library-wide configuration, so every existing
    /// SDK consumer sees exactly today's behaviour (back-compat for the published API).
    /// </summary>
    /// <param name="globalRerank">The library-wide rerank setting under test.</param>
    /// <param name="expectedFirst">The document expected first when the switch is unset.</param>
    [Theory]
    [InlineData(true, "doc-c")]
    [InlineData(false, "doc-a")]
    public async Task UnsetPerCallRerankFollowsGlobalConfiguration(bool globalRerank, string expectedFirst)
    {
        var client = CreateClient(globalRerank);

        var viaOptions = await client.SearchAsync("q", new SearchOptions { TopK = 3 });
        var viaLegacySignature = await client.SearchAsync("q", 3);

        Assert.Equal(expectedFirst, viaOptions[0].Chunk.DocumentId);
        Assert.Equal(expectedFirst, viaLegacySignature[0].Chunk.DocumentId);
    }

    /// <summary>
    /// Forcing rerank on without a configured reranker degrades to vector-similarity order rather
    /// than throwing, so the workspace toggle is safe to enable before a reranker is wired up.
    /// </summary>
    [Fact]
    public async Task PerCallRerankTrueWithoutRerankerReturnsVectorOrder()
    {
        var client = CreateClient(globalRerank: false, withReranker: false);

        var results = await client.SearchAsync("q", new SearchOptions { TopK = 3, Rerank = true });

        Assert.Equal(["doc-a", "doc-b", "doc-c"], results.Select(r => r.Chunk.DocumentId));
    }

    private static TechieRagClient CreateClient(bool globalRerank, bool withReranker = true)
    {
        var config = new TechieRagConfig();
        config.Rerank.Enabled = globalRerank;

        var results = new[]
        {
            TestData.Result("doc-a", "alpha", 0.9f),
            TestData.Result("doc-b", "beta", 0.8f),
            TestData.Result("doc-c", "gamma", 0.7f)
        };

        return new TechieRagClient(
            new FakeVectorStore(results),
            new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            config,
            NullLogger<TechieRagClient>.Instance,
            reranker: withReranker ? new ReversingReranker() : null);
    }
}
