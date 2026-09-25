using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Reranking;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Reranking;

/// <summary>
/// REQ-RAG-097 / BRD-144: with the Cohere API reranker enabled, a search returns its results in
/// the reranker's score order rather than the vector store's.
/// </summary>
/// <remarks>
/// The Cohere endpoint is a stubbed <see cref="HttpMessageHandler"/> answering Cohere's real
/// <c>/v2/rerank</c> response shape, so the whole path from <c>SearchAsync</c> through the reranker
/// runs with no network. The ONNX cross-encoder's own ranking is proven by the live
/// <c>LiveOnnxRerankerTests</c>, which need the model staged.
/// </remarks>
public sealed class RerankedSearchTests
{
    /// <summary>
    /// The vector store ranks alpha, beta, gamma; Cohere scores gamma highest, then alpha, then beta.
    /// A search on an instance with reranking enabled returns gamma, alpha, beta, each carrying the
    /// reranker's score.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-097 SearchReturnsTheRerankersOrder")]
    public async Task SearchReturnsTheRerankersOrder()
    {
        var cohere = new StubHandler("""
            {"results":[
                {"index":2,"relevance_score":0.97},
                {"index":0,"relevance_score":0.64},
                {"index":1,"relevance_score":0.11}
            ]}
            """);
        var config = new TechieRagConfig();
        config.Rerank.Enabled = true;
        var client = new TechieRagClient(
            new FakeVectorStore(
            [
                TestData.Result("doc-alpha", "alpha", 0.9f),
                TestData.Result("doc-beta", "beta", 0.8f),
                TestData.Result("doc-gamma", "gamma", 0.7f)
            ]),
            new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            config,
            NullLogger<TechieRagClient>.Instance,
            reranker: new CohereReranker(new HttpClient(cohere) { BaseAddress = new Uri("https://api.cohere.test") }));

        var results = await client.SearchAsync("which one?", 3);

        Assert.Equal(1, cohere.CallCount);
        Assert.Equal(["doc-gamma", "doc-alpha", "doc-beta"], results.Select(r => r.Chunk.DocumentId));
        Assert.Equal([0.97f, 0.64f, 0.11f], results.Select(r => r.Score));
    }

    private sealed class StubHandler(string responseJson) : HttpMessageHandler
    {
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
