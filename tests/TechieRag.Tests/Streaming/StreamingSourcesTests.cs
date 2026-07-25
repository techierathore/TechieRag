using Microsoft.Extensions.Logging.Abstractions;
using TechieRag;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Processors;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Streaming;

/// <summary>
/// Tests for streaming RAG that carries its retrieval sources (REQ-RAG-024) and for the
/// reranker toggle wired into retrieval (REQ-RAG-025). Drives the real
/// <see cref="TechieRagClient"/> with in-memory fakes for embedding, vector store, and LLM.
/// </summary>
public class StreamingSourcesTests
{
    /// <summary>
    /// AskStreamWithSourcesAsync yields the sources first, then answer tokens, then a final
    /// completed event carrying the aggregated answer — so a streamed response carries its
    /// citations without an app-side workaround.
    /// </summary>
    [Fact]
    public async Task StreamingYieldsSourcesThenTokensThenCompleted()
    {
        var results = new[]
        {
            TestData.Result("doc-1", "first passage", 0.9f),
            TestData.Result("doc-2", "second passage", 0.8f)
        };
        var client = CreateClient(results, out _, "Hello", ", ", "world");

        var events = new List<RagStreamEvent>();
        await foreach (var evt in client.AskStreamWithSourcesAsync("what is up?"))
        {
            events.Add(evt);
        }

        // First event: sources.
        Assert.Equal(RagStreamEventType.Sources, events[0].Type);
        Assert.NotNull(events[0].Sources);
        Assert.Equal(2, events[0].Sources!.Count);
        Assert.Equal("doc-1", events[0].Sources![0].Chunk.DocumentId);

        // Middle events: tokens.
        var tokenEvents = events.Where(e => e.Type == RagStreamEventType.Token).ToList();
        Assert.Equal(3, tokenEvents.Count);

        // Last event: completed with aggregated answer.
        Assert.Equal(RagStreamEventType.Completed, events[^1].Type);
        Assert.Equal("Hello, world", events[^1].Answer);
    }

    /// <summary>
    /// The streamed RAG prompt is built through the configured PromptTemplateEngine: the last
    /// message the LLM receives is the user query, and the retrieved context appears in the prompt.
    /// </summary>
    [Fact]
    public async Task StreamingHonorsPromptTemplate()
    {
        var results = new[] { TestData.Result("doc-1", "the sky is blue", 0.9f) };
        var client = CreateClient(results, out var llm, "ok");

        await foreach (var _ in client.AskStreamWithSourcesAsync("why is the sky blue?")) { }

        Assert.NotNull(llm.LastMessages);
        var promptText = string.Join("\n", llm.LastMessages!.Select(m => m.Content));
        Assert.Contains("the sky is blue", promptText);
        Assert.Contains("why is the sky blue?", promptText);
    }

    /// <summary>
    /// When reranking is enabled, the configured reranker reorders retrieval results before
    /// they are streamed as sources (REQ-RAG-025 toggle in the retrieval pipeline).
    /// </summary>
    [Fact]
    public async Task RerankerReordersStreamedSources()
    {
        var results = new[]
        {
            TestData.Result("doc-1", "alpha", 0.9f),
            TestData.Result("doc-2", "beta", 0.8f)
        };

        var config = new TechieRagConfig();
        config.Rerank.Enabled = true;
        config.Rerank.CandidateCount = 10;

        var llm = new FakeStreamingLlmProvider("done");
        var client = new TechieRagClient(
            new FakeVectorStore(results),
            new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            config,
            NullLogger<TechieRagClient>.Instance,
            llmProvider: llm,
            reranker: new ReversingReranker());

        RagStreamEvent? sourcesEvent = null;
        await foreach (var evt in client.AskStreamWithSourcesAsync("q", topK: 2))
        {
            if (evt.Type == RagStreamEventType.Sources) sourcesEvent = evt;
        }

        Assert.NotNull(sourcesEvent);
        // ReversingReranker flips order: beta now comes first.
        Assert.Equal("beta", sourcesEvent!.Sources![0].Chunk.Text);
        Assert.Equal("alpha", sourcesEvent.Sources![1].Chunk.Text);
    }

    private static TechieRagClient CreateClient(
        IReadOnlyList<SearchResult> results,
        out FakeStreamingLlmProvider llm,
        params string[] tokens)
    {
        llm = new FakeStreamingLlmProvider(tokens);
        return new TechieRagClient(
            new FakeVectorStore(results),
            new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            new TechieRagConfig(),
            NullLogger<TechieRagClient>.Instance,
            llmProvider: llm);
    }
}
