using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Agents.Tests.TestDoubles;

/// <summary>
/// Builds a real <see cref="TechieRagClient"/> over an in-memory store that returns canned passages,
/// so the agent is tested against the actual TechieRag retrieval path.
/// </summary>
public static class AgentTestRag
{
    /// <summary>The passage every search returns first.</summary>
    public const string RefundPassage = "Refunds are accepted within 30 days of purchase with a receipt.";

    /// <summary>Creates the TechieRag instance.</summary>
    /// <param name="llmProvider">The LLM configured on it, or null for none.</param>
    /// <returns>The instance.</returns>
    public static TechieRagClient Create(ILlmProvider? llmProvider = null)
    {
        var passage = new SearchResult
        {
            Chunk = new TextChunk { Id = "chunk-1", DocumentId = "doc-1", Text = RefundPassage, PageNumber = 3, Metadata = { ["DocumentName"] = "Returns Policy 2026.pdf" } },
            Score = 0.83f
        };

        return new TechieRagClient(
            new InMemoryStore([passage]),
            new FixedEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            new TechieRagConfig(),
            NullLogger<TechieRagClient>.Instance,
            llmProvider);
    }

    private sealed class FixedEmbeddingProvider : IEmbeddingProvider
    {
        public string Name => "Fixed";

        public string ModelName => "fixed";

        public int Dimensions => 3;

#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
        public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;
#pragma warning restore CS0067

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f });

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 0.1f, 0.2f, 0.3f }).ToList());
    }

    private sealed class InMemoryStore(IReadOnlyList<SearchResult> results) : IVectorStore
    {
        public string Name => "InMemory";

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> UpsertAsync(TextChunk chunk, CancellationToken cancellationToken = default) => Task.FromResult(chunk.Id);

        public Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(chunks.Select(c => c.Id).ToList());

        public Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchResult>>(results.Where(r => documentFilter is null || r.Chunk.DocumentId == documentFilter).Take(topK).ToList());

        public Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Document>>([new Document { Id = "doc-1", Name = "Returns Policy 2026.pdf", SourcePath = "policy.pdf", ChunkCount = 1 }]);

        public Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new IngestionStats());

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
