using System.Runtime.CompilerServices;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Tests.TestDoubles;

/// <summary>
/// Deterministic in-memory embedding provider used by client-level tests. Produces a fixed
/// small vector so no external embedding service is required.
/// </summary>
public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    /// <inheritdoc/>
    public string Name => "Fake";

    /// <inheritdoc/>
    public string ModelName => "fake-embed";

    /// <inheritdoc/>
    public int Dimensions => 3;

    /// <inheritdoc/>
#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;
#pragma warning restore CS0067

    /// <inheritdoc/>
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f });

    /// <inheritdoc/>
    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 0.1f, 0.2f, 0.3f }).ToList());
}

/// <summary>
/// In-memory vector store that returns a fixed, pre-seeded list of search results, letting
/// tests drive retrieval without a real database.
/// </summary>
public sealed class FakeVectorStore : IVectorStore
{
    private readonly List<SearchResult> results;

    /// <summary>Creates a fake store that returns the given results from every search.</summary>
    /// <param name="results">The results to return, highest relevance first.</param>
    public FakeVectorStore(IEnumerable<SearchResult> results) => this.results = results.ToList();

    /// <inheritdoc/>
    public string Name => "FakeVectorStore";

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<string> UpsertAsync(TextChunk chunk, CancellationToken cancellationToken = default) =>
        Task.FromResult(chunk.Id);

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(chunks.Select(c => c.Id).ToList());

    /// <inheritdoc/>
    public Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default)
    {
        var filtered = documentFilter is null
            ? results
            : results.Where(r => r.Chunk.DocumentId == documentFilter).ToList();
        return Task.FromResult<IReadOnlyList<SearchResult>>(filtered.Take(topK).ToList());
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Document>>([]);

    /// <inheritdoc/>
    public Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new IngestionStats());

    /// <inheritdoc/>
    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Scripted LLM provider that streams a fixed sequence of tokens and records the last
/// messages it received, so streaming and prompt-construction behavior can be asserted.
/// </summary>
public sealed class FakeStreamingLlmProvider : ILlmProvider
{
    private readonly string[] tokens;

    /// <summary>Creates a provider that yields the given tokens from every stream call.</summary>
    /// <param name="tokens">The tokens to stream in order.</param>
    public FakeStreamingLlmProvider(params string[] tokens) => this.tokens = tokens;

    /// <summary>Gets the messages passed to the most recent chat/stream call.</summary>
    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    /// <inheritdoc/>
    public string Name => "FakeLlm";

    /// <inheritdoc/>
    public string ModelName => "fake-model";

    /// <inheritdoc/>
    public bool SupportsToolCalling => false;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;
#pragma warning restore CS0067

    /// <inheritdoc/>
    public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new LlmResponse { Content = string.Concat(tokens), Usage = new TokenUsage() });

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }

    /// <inheritdoc/>
    public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastMessages = messages;
        return Task.FromResult(new LlmResponse { Content = string.Concat(tokens), Usage = new TokenUsage(), ModelName = ModelName });
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastMessages = messages;
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }

    /// <inheritdoc/>
    public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public int EstimateTokenCount(string text) => Math.Max(1, text.Length / 4);
}

/// <summary>
/// Reranker test double that reverses the input order, so tests can prove the reranker was
/// applied to the retrieval pipeline.
/// </summary>
public sealed class ReversingReranker : IReranker
{
    /// <inheritdoc/>
    public string Name => "Reversing";

    /// <inheritdoc/>
    public Task<IReadOnlyList<SearchResult>> RerankAsync(string query, IReadOnlyList<SearchResult> results, int topN, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchResult>>(results.Reverse().Take(topN).ToList());
}

/// <summary>Small helpers for building search results in tests.</summary>
public static class TestData
{
    /// <summary>Builds a search result for the given document/text/score.</summary>
    /// <param name="documentId">The owning document id.</param>
    /// <param name="text">The chunk text.</param>
    /// <param name="score">The relevance score.</param>
    /// <returns>A populated search result.</returns>
    public static SearchResult Result(string documentId, string text, float score) => new()
    {
        Chunk = new TextChunk { DocumentId = documentId, Text = text },
        Score = score
    };
}
