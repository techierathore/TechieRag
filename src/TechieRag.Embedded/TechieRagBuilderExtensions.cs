namespace TechieRag.Embedded;

/// <summary>
/// Extension methods for TechieRagBuilder to configure embedded ONNX embedding models.
/// </summary>
public static class TechieRagBuilderExtensions
{
    /// <summary>
    /// Configures TechieRag to use the BGE-M3 embedding model with auto-download.
    /// Model is downloaded on first use (~2.3GB) and cached locally.
    /// </summary>
    /// <param name="builder">The TechieRag builder instance.</param>
    /// <returns>The builder instance for chaining.</returns>
    /// <remarks>
    /// <para><b>Model:</b> BGE-M3 (1024 dimensions, multilingual, best quality)</para>
    /// <para><b>First Use:</b> Downloads ~2.3GB model (cached for subsequent uses)</para>
    /// <para><b>Example:</b></para>
    /// <code>
    /// var rag = new TechieRagBuilder()
    ///     .UseEmbedded()
    ///     .UseSqliteVec()
    ///     .Build();
    /// </code>
    /// </remarks>
    public static TechieRagBuilder UseEmbedded(this TechieRagBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Set the config to indicate we're using Embedded source
        // This ensures Settings page shows correct selection
        var config = builder.GetConfig();
        config.Embedding.Source = EmbeddingSource.Embedded;
        config.Embedding.Model = "bge-m3";

        // Register the EmbeddedEmbeddingProvider factory
        // This creates the BGE-M3 provider with auto-download on first use
        builder.UseCustomEmbeddingProvider(() => EmbeddedEmbeddingProvider.CreateDefault());

        return builder;
    }

    /// <summary>
    /// Configures TechieRag to use an embedded ONNX model for embeddings.
    /// This enables completely offline operation without external services.
    /// </summary>
    /// <param name="builder">The TechieRag builder instance.</param>
    /// <param name="modelDirectory">Directory containing model.onnx and tokenizer files.</param>
    /// <param name="dimensions">Embedding dimensions (default: 384 for MiniLM, 1024 for BGE-M3).</param>
    /// <param name="maxSequenceLength">Maximum token sequence length (default: 512).</param>
    /// <returns>The builder instance for chaining.</returns>
    /// <remarks>
    /// <para><b>Example:</b></para>
    /// <code>
    /// var rag = new TechieRagBuilder()
    ///     .UseEmbeddedModel("./models/all-MiniLM-L6-v2")
    ///     .UseSqliteVec()
    ///     .Build();
    /// </code>
    /// </remarks>
    public static TechieRagBuilder UseEmbeddedModel(
        this TechieRagBuilder builder,
        string modelDirectory,
        int dimensions = 384,
        int maxSequenceLength = 512)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);

        // Store configuration for later instantiation
        var config = builder.GetConfig();
        config.Embedding.Source = EmbeddingSource.Onnx;
        config.Embedding.ModelPath = modelDirectory;

        // Store dimensions in metadata for the builder to use
        config.Embedding.Model = $"embedded-onnx-{dimensions}d";

        return builder;
    }

    /// <summary>
    /// Configures TechieRag to use the all-MiniLM-L6-v2 model if available in the default location.
    /// </summary>
    /// <param name="builder">The TechieRag builder instance.</param>
    /// <param name="modelDirectory">Optional custom model directory. Defaults to ./models/all-MiniLM-L6-v2</param>
    /// <returns>The builder instance for chaining.</returns>
    public static TechieRagBuilder UseMiniLM(
        this TechieRagBuilder builder,
        string? modelDirectory = null)
    {
        var path = modelDirectory ?? Path.Combine(AppContext.BaseDirectory, "models", "all-MiniLM-L6-v2");
        return builder.UseEmbeddedModel(path, dimensions: 384);
    }

    /// <summary>
    /// Configures TechieRag to use the BGE-Small model if available.
    /// </summary>
    /// <param name="builder">The TechieRag builder instance.</param>
    /// <param name="modelDirectory">Optional custom model directory.</param>
    /// <returns>The builder instance for chaining.</returns>
    public static TechieRagBuilder UseBgeSmall(
        this TechieRagBuilder builder,
        string? modelDirectory = null)
    {
        var path = modelDirectory ?? Path.Combine(AppContext.BaseDirectory, "models", "bge-small-en-v1.5");
        return builder.UseEmbeddedModel(path, dimensions: 384);
    }

    /// <summary>
    /// Enables the rerank stage using the local BGE-Reranker-v2-M3 ONNX cross-encoder
    /// (auto-downloaded on first use).
    /// </summary>
    /// <param name="builder">The TechieRag builder instance.</param>
    /// <param name="modelDirectory">Optional pre-downloaded model directory; null downloads on first use.</param>
    /// <param name="topN">How many results the reranker returns (0 = same as requested topK).</param>
    /// <param name="candidateCount">How many vector search candidates are fetched for reranking.</param>
    /// <returns>The builder instance for chaining.</returns>
    /// <remarks>
    /// <para><b>Example:</b></para>
    /// <code>
    /// var rag = new TechieRagBuilder()
    ///     .UseEmbedded()
    ///     .UseSqliteVec()
    ///     .UseEmbeddedReranker()
    ///     .Build();
    /// </code>
    /// </remarks>
    public static TechieRagBuilder UseEmbeddedReranker(
        this TechieRagBuilder builder,
        string? modelDirectory = null,
        int topN = 0,
        int candidateCount = 20)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var config = builder.GetConfig();
        config.Rerank.Source = RerankSource.LocalOnnx;
        config.Rerank.ModelPath = modelDirectory;

        return builder.WithReranker(
            () => modelDirectory is null
                ? new OnnxCrossEncoderReranker()
                : OnnxCrossEncoderReranker.FromDirectory(modelDirectory),
            topN,
            candidateCount);
    }

    /// <summary>
    /// Creates an EmbeddedEmbeddingProvider instance from the specified model directory.
    /// </summary>
    /// <param name="modelDirectory">Directory containing the ONNX model and tokenizer files.</param>
    /// <param name="dimensions">Embedding dimensions.</param>
    /// <param name="maxSequenceLength">Maximum sequence length.</param>
    /// <returns>A configured EmbeddedEmbeddingProvider instance.</returns>
    public static EmbeddedEmbeddingProvider CreateEmbeddedProvider(
        string modelDirectory,
        int dimensions = 384,
        int maxSequenceLength = 512)
    {
        return new EmbeddedEmbeddingProvider(modelDirectory, dimensions, maxSequenceLength);
    }
}
