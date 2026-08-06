namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for text embedding generation services.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a unified interface for converting text into vector embeddings
/// across different embedding providers (ONNX, Ollama, LM Studio, Azure OpenAI).</para>
/// <para><b>Code Flow:</b> Instantiated by TechieRagBuilder based on configuration. Called by
/// TechieRagClient during ingestion (to embed chunks) and search (to embed queries).</para>
/// <para><b>Implementations:</b> OnnxEmbeddingProvider, OllamaEmbeddingProvider,
/// LmStudioEmbeddingProvider, AzureOpenAIEmbeddingProvider</para>
/// </remarks>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Gets the display name of this embedding provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the name of the embedding model being used.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Gets the dimensionality of the embedding vectors produced.
    /// </summary>
    /// <remarks>BGE-M3 produces 1024-dimensional vectors.</remarks>
    int Dimensions { get; }

    /// <summary>
    /// Gets a stable identifier for what this provider produces — provider, model and encoding
    /// revision (REQ-RAG-052).
    /// </summary>
    /// <remarks>
    /// <para><b>What it is for.</b> Vectors produced by different providers, different models, or the
    /// same model encoded differently live in different spaces, and cosine similarity across them is
    /// meaningless. Ingestion stamps this on every document so
    /// <see cref="Models.EmbeddingStaleness"/> can tell a consumer that a stored corpus no longer
    /// matches what queries are embedded with — the situation that is otherwise invisible, because
    /// retrieval keeps returning results and they are simply wrong.</para>
    /// <para><b>Bump the revision whenever identical input starts producing different vectors.</b>
    /// The case that prompted this was neither a new provider nor a new model: BGE-M3's tokenization
    /// was corrected (TR-RAG-044), so provider and model alone would not have detected it.</para>
    /// <para><b>Default: <see cref="Models.EmbeddingStaleness.UnknownSignature"/>.</b> A provider
    /// that has not opted in reports "unknown", and the analysis then says it cannot determine
    /// anything rather than reporting a clean result it never established. Defaulted rather than
    /// required so existing implementations outside this repository keep compiling
    /// (REQ-NFR-007).</para>
    /// </remarks>
    string EmbeddingSignature => Models.EmbeddingStaleness.UnknownSignature;

    /// <summary>
    /// Generates an embedding vector for a single text input.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A float array representing the embedding vector.</returns>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple texts in a batch operation.
    /// </summary>
    /// <param name="texts">Collection of texts to embed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of embedding vectors corresponding to each input text.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Batch operations are more efficient for multiple texts
    /// as they reduce API call overhead.</para>
    /// </remarks>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised after each embedding operation completes, for telemetry purposes.
    /// </summary>
    event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;
}

/// <summary>
/// Event arguments for embedding completion telemetry.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides metrics about embedding operations for logging,
/// monitoring, and token usage tracking.</para>
/// </remarks>
public class EmbeddingCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the approximate number of tokens processed.
    /// </summary>
    public required int TokenCount { get; init; }

    /// <summary>
    /// Gets the number of text inputs embedded.
    /// </summary>
    public required int TextCount { get; init; }

    /// <summary>
    /// Gets the duration of the embedding operation.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the name of the embedding model used.
    /// </summary>
    public required string ModelName { get; init; }

    /// <summary>
    /// Gets the name of the embedding provider used.
    /// </summary>
    public required string ProviderName { get; init; }
}
