using TechieRag.Abstractions;
using TechieRag.Embedded;
using TechieRag.Models;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Reranking.Live;

/// <summary>
/// Loads the 2.28 GB cross-encoder once for the whole class.
/// </summary>
/// <remarks>
/// One <c>InferenceSession</c> over a 2.27 GB graph costs seconds to build and holds the weights in
/// memory; building it per test would dominate the run for no added assurance. The fixture is
/// created only when the model is staged — every test that uses it is gated by
/// <see cref="LiveRerankerFactAttribute"/>.
/// </remarks>
public sealed class OnnxRerankerFixture : IDisposable
{
    /// <summary>Initializes a new instance of the <see cref="OnnxRerankerFixture"/> class.</summary>
    public OnnxRerankerFixture()
    {
        if (!LiveRerankerFactAttribute.IsModelStaged) return;

        Reranker = OnnxCrossEncoderReranker.FromDirectory(LiveRerankerFactAttribute.ModelDirectory);
    }

    /// <summary>Gets the loaded reranker, or null when no model is staged.</summary>
    public OnnxCrossEncoderReranker? Reranker { get; }

    /// <inheritdoc />
    public void Dispose() => Reranker?.Dispose();
}

/// <summary>
/// The claim BRD-106 / REQ-RAG-025 actually makes: the LOCAL ONNX cross-encoder reranks.
/// </summary>
/// <remarks>
/// <para><b>This is the half that could not be faked.</b> <c>OnnxCrossEncoderRerankerTests</c> covers
/// the surface — validation, short circuits, failure messages — but every one of those assertions
/// holds equally well for a class that can never load a model. Until these ran,
/// <c>OnnxCrossEncoderReranker</c> had never scored a single pair, and the row said so.</para>
/// <para><b>It had never worked, either.</b> The download URL pointed at <c>BAAI/bge-reranker-v2-m3</c>,
/// which publishes no ONNX export — <c>onnx/model.onnx</c> returned HTTP 404 — and the file list
/// omitted the external-data sidecar that carries the weights. Both were fixed to get here; these
/// tests are what stops that regressing into a component nobody notices is dead.</para>
/// <para><b>Assertions are on ORDER, not on absolute scores.</b> A cross-encoder's logits are not
/// calibrated to a fixed range, and pinning one would make the suite fail on a future export that
/// ranks identically. What a reranker promises is that the better passage comes first.</para>
/// </remarks>
[Trait("Category", LiveRerankerFactAttribute.CategoryName)]
[Collection(OnnxModelCollection.Name)]
public sealed class LiveOnnxRerankerTests(OnnxRerankerFixture fixture) : IClassFixture<OnnxRerankerFixture>
{
    /// <summary>
    /// The core promise: a relevant passage that arrived LAST comes back FIRST.
    /// </summary>
    /// <remarks>
    /// The candidates are handed over in deliberately wrong order, with vector scores that disagree
    /// with relevance — the exact situation a reranker exists for. If this passes, the model ran and
    /// its output changed the ranking; nothing else in the suite can establish that.
    /// </remarks>
    [LiveRerankerFact]
    public async Task ItPromotesTheRelevantPassageOverTheIrrelevantOnes()
    {
        var reranker = fixture.Reranker!;

        var candidates = new List<SearchResult>
        {
            TestData.Result("doc-a", "The Amazon rainforest produces a large share of the world's oxygen.", 0.91f),
            TestData.Result("doc-b", "Sourdough needs a starter of flour and water left to ferment.", 0.88f),
            TestData.Result("doc-c", "To change a car tyre, loosen the nuts before you jack the car up.", 0.85f),
            TestData.Result("doc-d", "Postgres stores vectors with the pgvector extension and indexes them with HNSW.", 0.42f)
        };

        var ranked = await reranker.RerankAsync(
            "How do I store embeddings in a Postgres database?", candidates, topN: 4);

        Assert.Equal("doc-d", ranked[0].Chunk.DocumentId);

        // And it is not merely first — it genuinely outscores what the vector search preferred.
        Assert.True(
            ranked[0].Score > ranked[1].Score,
            $"The top result did not outscore the second: {ranked[0].Score} vs {ranked[1].Score}.");
    }

    /// <summary>Scores are real per-pair values, not one constant handed back for everything.</summary>
    /// <remarks>
    /// A model that failed to load and silently returned zeros would satisfy an order assertion by
    /// luck often enough to look fine. Distinct scores prove per-pair inference actually happened.
    /// </remarks>
    [LiveRerankerFact]
    public async Task ItProducesADistinctScorePerPair()
    {
        var reranker = fixture.Reranker!;

        var ranked = await reranker.RerankAsync(
            "What is the capital of France?",
            [
                TestData.Result("doc-a", "Paris is the capital and most populous city of France.", 0.5f),
                TestData.Result("doc-b", "A dry martini is made with gin and vermouth.", 0.5f)
            ],
            topN: 2);

        Assert.Equal("doc-a", ranked[0].Chunk.DocumentId);
        Assert.Distinct(ranked.Select(result => result.Score));
    }

    /// <summary><c>topN</c> truncates to the best N, and returns them in order.</summary>
    [LiveRerankerFact]
    public async Task ItReturnsOnlyTheTopNInDescendingOrder()
    {
        var reranker = fixture.Reranker!;

        var ranked = await reranker.RerankAsync(
            "vector database indexing",
            [
                TestData.Result("doc-a", "HNSW is a graph index used for approximate nearest-neighbour search.", 0.5f),
                TestData.Result("doc-b", "The recipe calls for two eggs and a cup of milk.", 0.5f),
                TestData.Result("doc-c", "Qdrant is a vector database written in Rust.", 0.5f),
                TestData.Result("doc-d", "Badgers are nocturnal mammals native to Europe.", 0.5f)
            ],
            topN: 2);

        Assert.Equal(2, ranked.Count);
        Assert.True(ranked[0].Score >= ranked[1].Score, "Results came back out of score order.");

        // The two kept are the two on topic, not an arbitrary pair.
        Assert.Equal(["doc-a", "doc-c"], ranked.Select(r => r.Chunk.DocumentId).Order());
    }

    /// <summary>Asking for more than exists returns everything, without padding or throwing.</summary>
    [LiveRerankerFact]
    public async Task ATopNLargerThanTheCandidateSetReturnsThemAll()
    {
        var reranker = fixture.Reranker!;

        var ranked = await reranker.RerankAsync(
            "anything at all",
            [TestData.Result("doc-a", "one", 0.5f), TestData.Result("doc-b", "two", 0.5f)],
            topN: 50);

        Assert.Equal(2, ranked.Count);
    }

    /// <summary>
    /// It ranks across languages, which is the reason BGE-M3 was chosen and the reason this product
    /// can ship a Hindi UI over an English corpus.
    /// </summary>
    /// <remarks>
    /// A Hindi query against English passages. A monolingual cross-encoder scores this near-randomly;
    /// BGE-M3 is trained on 100+ languages, and REQ-NFR-006 claims that capability.
    /// </remarks>
    [LiveRerankerFact]
    public async Task ItRanksAcrossLanguages()
    {
        var reranker = fixture.Reranker!;

        var ranked = await reranker.RerankAsync(
            "फ्रांस की राजधानी क्या है?",
            [
                TestData.Result("doc-a", "Bicycles should have their chains oiled regularly.", 0.5f),
                TestData.Result("doc-b", "Paris is the capital city of France.", 0.5f),
                TestData.Result("doc-c", "The blue whale is the largest animal on Earth.", 0.5f)
            ],
            topN: 3);

        Assert.Equal("doc-b", ranked[0].Chunk.DocumentId);
    }

    /// <summary>The same input ranks the same way twice — inference here is deterministic.</summary>
    /// <remarks>
    /// Worth pinning: a reranker that reordered results between identical searches would make the
    /// citation panel unstable for a user who simply asked again.
    /// </remarks>
    [LiveRerankerFact]
    public async Task TheSameQueryRanksTheSameWayTwice()
    {
        var reranker = fixture.Reranker!;

        var candidates = new List<SearchResult>
        {
            TestData.Result("doc-a", "Retrieval-augmented generation grounds an answer in retrieved context.", 0.5f),
            TestData.Result("doc-b", "Cast iron pans should not be left to soak.", 0.5f),
            TestData.Result("doc-c", "Chunk overlap keeps a sentence from being split across two chunks.", 0.5f)
        };

        var first = await reranker.RerankAsync("what is RAG?", candidates, topN: 3);
        var second = await reranker.RerankAsync("what is RAG?", candidates, topN: 3);

        Assert.Equal(
            first.Select(r => r.Chunk.DocumentId),
            second.Select(r => r.Chunk.DocumentId));
        Assert.Equal(first.Select(r => r.Score), second.Select(r => r.Score));
    }

    /// <summary>It satisfies the <see cref="IReranker"/> contract the rest of the library binds to.</summary>
    /// <remarks>
    /// <c>SearchAsync</c>'s rerank switch resolves an <see cref="IReranker"/>, so the local
    /// cross-encoder has to be usable through that interface and not only through its own type.
    /// </remarks>
    [LiveRerankerFact]
    public async Task ItWorksThroughTheIRerankerAbstraction()
    {
        IReranker reranker = fixture.Reranker!;

        var ranked = await reranker.RerankAsync(
            "pgvector",
            [
                TestData.Result("doc-a", "Knitting needles come in several gauges.", 0.5f),
                TestData.Result("doc-b", "pgvector adds a vector column type to PostgreSQL.", 0.5f)
            ],
            topN: 1);

        Assert.Equal("doc-b", Assert.Single(ranked).Chunk.DocumentId);
        Assert.Equal("Embedded-ONNX-CrossEncoder", reranker.Name);
    }
}
