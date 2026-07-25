using System.Net;
using System.Text;
using TechieRag.Models;
using TechieRag.Reranking;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Reranking;

/// <summary>
/// Unit tests for the API reranker contract via <see cref="CohereReranker"/> (REQ-RAG-025).
/// Uses a stubbed <see cref="HttpMessageHandler"/> so no network call is made: the reranker
/// must reorder the input results by the returned relevance scores and cap at topN.
/// </summary>
public class CohereRerankerTests
{
    /// <summary>An empty candidate list short-circuits without an HTTP call.</summary>
    [Fact]
    public async Task RerankReturnsEmptyForNoCandidates()
    {
        var handler = new StubHandler("""{"results":[]}""");
        var reranker = CreateReranker(handler);

        var result = await reranker.RerankAsync("q", [], 5);

        Assert.Empty(result);
        Assert.Equal(0, handler.CallCount);
    }

    /// <summary>The reranker reorders results by the service's relevance scores and caps at topN.</summary>
    [Fact]
    public async Task RerankReordersByRelevanceScore()
    {
        // Service honors top_n=2 and returns the two most relevant: index 2 then index 0.
        var responseJson = """
        {"results":[
            {"index":2,"relevance_score":0.95},
            {"index":0,"relevance_score":0.60}
        ]}
        """;
        var handler = new StubHandler(responseJson);
        var reranker = CreateReranker(handler);

        var candidates = new List<SearchResult>
        {
            TestData.Result("doc", "alpha", 0.5f),
            TestData.Result("doc", "beta", 0.5f),
            TestData.Result("doc", "gamma", 0.5f)
        };

        var reranked = await reranker.RerankAsync("query", candidates, topN: 2);

        Assert.Equal(2, reranked.Count);
        Assert.Equal("gamma", reranked[0].Chunk.Text);
        Assert.Equal(0.95f, reranked[0].Score);
        Assert.Equal("alpha", reranked[1].Chunk.Text);
        Assert.Equal(1, handler.CallCount);
    }

    /// <summary>Out-of-range indices returned by the service are ignored, not crashed on.</summary>
    [Fact]
    public async Task RerankIgnoresOutOfRangeIndices()
    {
        var handler = new StubHandler("""{"results":[{"index":9,"relevance_score":0.9},{"index":0,"relevance_score":0.4}]}""");
        var reranker = CreateReranker(handler);

        var candidates = new List<SearchResult> { TestData.Result("doc", "only", 0.3f) };

        var reranked = await reranker.RerankAsync("query", candidates, topN: 5);

        Assert.Single(reranked);
        Assert.Equal("only", reranked[0].Chunk.Text);
    }

    private static CohereReranker CreateReranker(StubHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.cohere.test") };
        return new CohereReranker(httpClient);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string responseJson;

        public StubHandler(string responseJson) => this.responseJson = responseJson;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
