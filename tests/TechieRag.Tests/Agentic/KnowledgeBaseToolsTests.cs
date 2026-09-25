using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Agentic;
using TechieRag.Models;
using TechieRag.Services;
using TechieRag.Tests.TestDoubles;
using Xunit;
using static TechieRag.Tests.TestDoubles.ScriptedEventLlmProvider;

namespace TechieRag.Tests.Agentic;

/// <summary>
/// Tests for the agentic retrieval contract in <c>TechieRag.Agentic</c> (REQ-RAG-015 / BRD-83).
/// </summary>
public class KnowledgeBaseToolsTests
{
    /// <summary>
    /// Acceptance: the knowledge-base tools registered on the agent loop through a <see cref="ToolRegistry"/>
    /// answer the model's <c>search_knowledge_base</c> call with refs, scores and a strong/weak/none status.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-015 RegisteredSearchReturnsRefsScoresAndStatus")]
    public async Task RegisteredSearchReturnsRefsScoresAndStatus()
    {
        var provider = new ScriptedEventLlmProvider(
            [Call("c1", "search_knowledge_base", """{"query":"refund window days"}"""), Done("tool_calls")],
            [Text("Refunds within 30 days [S1]."), Done()]);
        var state = new RetrievalTurnState();
        var registry = new ToolRegistry().RegisterKnowledgeBase(Source(Result("d1", "Refunds are accepted within 30 days.", 0.82f)), null, state);

        var response = await new AgentLoopRunner(provider, registry).RunAsync([ChatMessage.User("How long do I have to return?")]);

        using var result = JsonDocument.Parse(provider.Calls[1][^1].Content!);
        var root = result.RootElement;
        Assert.Equal("strong", root.GetProperty("status").GetString());
        Assert.Equal(0.82, root.GetProperty("best_score").GetDouble());
        var passage = root.GetProperty("results")[0];
        Assert.Equal("S1", passage.GetProperty("ref").GetString());
        Assert.Equal(0.82, passage.GetProperty("score").GetDouble());
        Assert.Equal("Refunds within 30 days [S1].", response.Content);
    }

    /// <summary>The loop offers both tools to the model with the contract's descriptions and schemas.</summary>
    [Fact]
    public async Task RegisteredToolsAreOfferedToModel()
    {
        var provider = new ScriptedEventLlmProvider([Text("Hello."), Done()]);
        var registry = new ToolRegistry().RegisterKnowledgeBase(Source(), null, new RetrievalTurnState());

        await new AgentLoopRunner(provider, registry).RunAsync([ChatMessage.User("hi")]);

        var tools = provider.Options[0]!.Tools!;
        Assert.Equal(["search_knowledge_base", "list_documents"], tools.Select(t => t.Name));
        Assert.Equal(KnowledgeBaseTools.SearchDescription, tools[0].Description);
        using var schema = JsonDocument.Parse(tools[0].ParametersSchema);
        Assert.Equal("query", schema.RootElement.GetProperty("required")[0].GetString());
    }

    /// <summary>A best score between the none and weak thresholds is weak, with the re-search hint naming the threshold.</summary>
    [Fact]
    public async Task WeakScoreGivesWeakStatusAndHint()
    {
        var json = await SearchAsync(Source(Result("d1", "tangential", 0.48f)));

        Assert.Equal("weak", json.GetProperty("status").GetString());
        Assert.StartsWith("No passage scored above 0.55.", json.GetProperty("hint").GetString());
    }

    /// <summary>
    /// When reranking classified the result as weak against the rerank threshold, the hint quotes that
    /// threshold, not the cosine one (0.55) that was never applied.
    /// </summary>
    [Fact]
    public async Task RerankedWeakHintQuotesRerankThreshold()
    {
        var json = await SearchAsync(Source(Result("d1", "tangential", 0.3f)), new RetrievalToolOptions { Rerank = true, RerankWeakThreshold = 0.5f });

        Assert.Equal("weak", json.GetProperty("status").GetString());
        Assert.StartsWith("No passage scored above 0.5.", json.GetProperty("hint").GetString());
    }

    /// <summary>A best score under the none threshold, or no results at all, is none.</summary>
    [Fact]
    public async Task LowScoreOrNoResultsGiveNone()
    {
        var low = await SearchAsync(Source(Result("d1", "unrelated", 0.2f)));
        var empty = await SearchAsync(Source());

        Assert.Equal("none", low.GetProperty("status").GetString());
        Assert.Equal("none", empty.GetProperty("status").GetString());
        Assert.Equal(0, empty.GetProperty("results").GetArrayLength());
    }

    /// <summary>The per-turn budget is enforced: the call after the last allowed one reports limit_reached without searching.</summary>
    [Fact]
    public async Task BudgetExhaustionReportsLimitReached()
    {
        var calls = 0;
        var source = new DelegateRetrievalSource((_, _, _, _) =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<SearchResult>>([Result("d1", "x", 0.9f)]);
        });
        var options = new RetrievalToolOptions { MaxSearchesPerTurn = 2 };
        var state = new RetrievalTurnState();

        await KnowledgeBaseTools.ExecuteSearchAsync(source, options, state, """{"query":"a"}""", default);
        var second = await KnowledgeBaseTools.ExecuteSearchAsync(source, options, state, """{"query":"b"}""", default);
        var third = await KnowledgeBaseTools.ExecuteSearchAsync(source, options, state, """{"query":"c"}""", default);

        Assert.Equal(0, JsonDocument.Parse(second).RootElement.GetProperty("searches_remaining").GetInt32());
        Assert.Equal("limit_reached", JsonDocument.Parse(third).RootElement.GetProperty("status").GetString());
        Assert.Equal(2, calls);
    }

    /// <summary>Refs continue across searches and turns, and a chunk returned twice keeps its first ref.</summary>
    [Fact]
    public async Task RefsAreStableAcrossSearchesAndTurns()
    {
        var first = Result("d1", "alpha", 0.9f);
        var second = Result("d2", "beta", 0.8f);
        var state = new RetrievalTurnState();
        var options = new RetrievalToolOptions();

        await KnowledgeBaseTools.ExecuteSearchAsync(Source(first), options, state, """{"query":"a"}""", default);
        state.BeginTurn();
        var json = await KnowledgeBaseTools.ExecuteSearchAsync(Source(second, first), options, state, """{"query":"b"}""", default);

        var refs = JsonDocument.Parse(json).RootElement.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("ref").GetString());
        Assert.Equal(["S2", "S1"], refs);
        Assert.Equal(2, state.Collected.Count);
        Assert.Equal(1, state.SearchesUsed);
    }

    /// <summary>Passage text is truncated to the configured length and the document name comes from chunk metadata.</summary>
    [Fact]
    public async Task ResultTruncatesTextAndNamesDocument()
    {
        var result = Result("d1", new string('x', 50), 0.9f);
        result.Chunk.Metadata["DocumentName"] = "Returns Policy 2026.pdf";
        result.Chunk.PageNumber = 3;

        var json = await SearchAsync(Source(result), new RetrievalToolOptions { MaxChunkChars = 10 });

        var passage = json.GetProperty("results")[0];
        Assert.Equal("Returns Policy 2026.pdf", passage.GetProperty("document").GetString());
        Assert.Equal(3, passage.GetProperty("page").GetInt32());
        Assert.Equal(11, passage.GetProperty("text").GetString()!.Length);
    }

    /// <summary>top_k is clamped to MaxTopK and document_id is passed to the source; a configured filter overrides it.</summary>
    [Fact]
    public async Task ArgumentsAreClampedAndFiltered()
    {
        (int TopK, string? Document) seen = default;
        var source = new DelegateRetrievalSource((_, topK, documentId, _) =>
        {
            seen = (topK, documentId);
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        });

        await KnowledgeBaseTools.ExecuteSearchAsync(source, new RetrievalToolOptions(), new RetrievalTurnState(), """{"query":"q","top_k":99,"document_id":"d7"}""", default);
        Assert.Equal((20, "d7"), seen);

        await KnowledgeBaseTools.ExecuteSearchAsync(source, new RetrievalToolOptions { DocumentFilter = "d1" }, new RetrievalTurnState(), """{"query":"q","document_id":"d7"}""", default);
        Assert.Equal("d1", seen.Document);
    }

    /// <summary>A call without a query is refused with a sentence the model can act on.</summary>
    [Fact]
    public async Task MissingQueryIsRefused()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            KnowledgeBaseTools.ExecuteSearchAsync(Source(), new RetrievalToolOptions(), new RetrievalTurnState(), """{"top_k":3}""", default));

        Assert.Contains("query", ex.Message);
    }

    /// <summary>Reranked results are classified on presence, not on the cosine thresholds, unless a rerank threshold is set.</summary>
    [Fact]
    public void RerankedScoresIgnoreCosineThresholds()
    {
        Assert.Equal("strong", KnowledgeBaseTools.Classify(0.01f, new RetrievalToolOptions { Rerank = true }));
        Assert.Equal("weak", KnowledgeBaseTools.Classify(0.01f, new RetrievalToolOptions { Rerank = true, RerankWeakThreshold = 0.5f }));
    }

    /// <summary>list_documents returns id, name and passage count per document.</summary>
    [Fact]
    public async Task ListReturnsDocuments()
    {
        var source = new DelegateRetrievalSource(
            (_, _, _, _) => Task.FromResult<IReadOnlyList<SearchResult>>([]),
            _ => Task.FromResult<IReadOnlyList<Document>>([new Document { Id = "d1", Name = "Policy.pdf", SourcePath = "p", ChunkCount = 14 }]));

        using var json = JsonDocument.Parse(await KnowledgeBaseTools.ExecuteListAsync(source, default));

        var document = json.RootElement.GetProperty("documents")[0];
        Assert.Equal("d1", document.GetProperty("id").GetString());
        Assert.Equal(14, document.GetProperty("passages").GetInt32());
    }

    /// <summary>
    /// The TechieRag source searches through the SearchOptions overload and states the rerank switch
    /// explicitly, so the global rerank setting cannot silently change the score scale under the tool.
    /// </summary>
    [Fact]
    public async Task TechieRagSourceStatesRerankSwitch()
    {
        var config = new TechieRagConfig();
        config.Rerank.Enabled = true;
        var client = new TechieRagClient(new FakeVectorStore([Result("d1", "alpha", 0.9f), Result("d2", "beta", 0.8f)]), new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(), config, NullLogger<TechieRagClient>.Instance, reranker: new ReversingReranker());

        var plain = await new TechieRagRetrievalSource(client, rerank: false).SearchAsync("q", 5, null, default);
        var reranked = await new TechieRagRetrievalSource(client, rerank: true).SearchAsync("q", 5, null, default);

        Assert.Equal(["alpha", "beta"], plain.Select(r => r.Chunk.Text));
        Assert.Equal(["beta", "alpha"], reranked.Select(r => r.Chunk.Text));
    }

    /// <summary>Domain guidance is appended after the numbered rules, which stay intact.</summary>
    [Fact]
    public void DomainGuidanceIsAppended()
    {
        var composed = AgenticInstructions.WithDomainGuidance("Answer as a support agent.");

        Assert.StartsWith(AgenticInstructions.Default, composed);
        Assert.EndsWith("DOMAIN GUIDANCE\nAnswer as a support agent.", composed);
        Assert.Equal(AgenticInstructions.Default, AgenticInstructions.WithDomainGuidance(" "));
    }

    private static async Task<JsonElement> SearchAsync(IRetrievalSource source, RetrievalToolOptions? options = null)
    {
        var json = await KnowledgeBaseTools.ExecuteSearchAsync(source, options ?? new RetrievalToolOptions(), new RetrievalTurnState(), """{"query":"q"}""", default);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static DelegateRetrievalSource Source(params SearchResult[] results) =>
        new((_, _, _, _) => Task.FromResult<IReadOnlyList<SearchResult>>(results));

    private static SearchResult Result(string documentId, string text, float score) => TestData.Result(documentId, text, score);
}
