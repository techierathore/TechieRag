using TechieRag.Models;

namespace TechieRag.Embedded;

/// <summary>
/// Extension methods for TechieRagBuilder to configure embedded ONNX embedding models.
/// </summary>
public static class TechieRagBuilderExtensions
{
    /// <summary>
    /// Configures TechieRag to use this platform's default embedded model, downloaded once into the
    /// model root: bge-m3 on desktops, all-MiniLM-L6-v2 on Android and iOS.
    /// </summary>
    /// <param name="builder">The TechieRag builder instance.</param>
    /// <returns>The builder instance for chaining.</returns>
    /// <remarks>
    /// <para><b>Desktop (Windows, macOS, Mac Catalyst, Linux):</b> BGE-M3 — 1024 dimensions,
    /// multilingual, a 2.3 GB first download.</para>
    /// <para><b>Android and iOS:</b> all-MiniLM-L6-v2 — 384 dimensions, English, a 91 MB first
    /// download (REQ-RAG-054 / BRD-91).</para>
    /// <para><b>Where:</b> <c>&lt;ModelRoot&gt;/&lt;model&gt;</c>, the per-user application data folder
    /// unless <see cref="UseModelRoot"/> moved it (REQ-RAG-053). The size is reported before the
    /// first byte through <see cref="ModelDownloadService.DownloadSizeKnown"/> (REQ-RAG-055).</para>
    /// <para><b>Example:</b></para>
    /// <code>
    /// var rag = new TechieRagBuilder()
    ///     .UseEmbedded()
    ///     .UseSqliteVec()
    ///     .Build();
    /// </code>
    /// </remarks>
    public static TechieRagBuilder UseEmbedded(this TechieRagBuilder builder) =>
        UseEmbedded(builder, EmbeddedModel.PlatformDefault);

    /// <summary>
    /// Configures TechieRag to use one embedded model, downloaded once into the model root.
    /// </summary>
    /// <param name="builder">The TechieRag builder instance.</param>
    /// <param name="model"><see cref="EmbeddedModel.BgeM3"/> or <see cref="EmbeddedModel.MiniLM"/>.</param>
    /// <returns>The builder instance for chaining.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="model"/> is bge-m3 and this is Android or iOS; the message names its 2.3 GB size.
    /// </exception>
    public static TechieRagBuilder UseEmbedded(this TechieRagBuilder builder, EmbeddedModel model) =>
        UseEmbedded(builder, model, EmbeddedModel.IsPhonePlatform);

    /// <summary>
    /// Registers an embedded model after checking it against a platform.
    /// </summary>
    /// <param name="builder">The TechieRag builder instance.</param>
    /// <param name="model">The model.</param>
    /// <param name="isPhone">Whether the platform is Android or iOS; a parameter so tests can ask for either.</param>
    /// <returns>The builder instance for chaining.</returns>
    internal static TechieRagBuilder UseEmbedded(TechieRagBuilder builder, EmbeddedModel model, bool isPhone)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(model);
        model.EnsureSupported(isPhone);

        // The config records the choice so a settings screen shows it.
        var config = builder.GetConfig();
        config.Embedding.Source = EmbeddingSource.Embedded;
        config.Embedding.Model = model.Name;

        builder.UseCustomEmbeddingProvider(() => new EmbeddedEmbeddingProvider(model));
        return builder;
    }

    /// <summary>
    /// Moves the folder every local model (embedding model, reranker, local language model) is
    /// downloaded to and loaded from, for the whole process.
    /// </summary>
    /// <param name="builder">The TechieRag builder instance.</param>
    /// <param name="modelRoot">The folder; each model gets a sub-folder named after it.</param>
    /// <returns>The builder instance for chaining.</returns>
    /// <remarks>Same as <see cref="ModelRoot.Set"/>; call it before the first model loads.</remarks>
    public static TechieRagBuilder UseModelRoot(this TechieRagBuilder builder, string modelRoot)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ModelRoot.Set(modelRoot);
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
    /// <param name="modelDirectory">
    /// Optional pre-staged model directory. When omitted, all-MiniLM-L6-v2 is downloaded once into
    /// <c>&lt;ModelRoot&gt;/all-minilm-l6-v2</c>, exactly as <c>UseEmbedded(EmbeddedModel.MiniLM)</c>.
    /// </param>
    /// <returns>The builder instance for chaining.</returns>
    public static TechieRagBuilder UseMiniLM(
        this TechieRagBuilder builder,
        string? modelDirectory = null)
    {
        return modelDirectory is null
            ? builder.UseEmbedded(EmbeddedModel.MiniLM)
            : builder.UseEmbeddedModel(modelDirectory, dimensions: 384);
    }

    /// <summary>
    /// Configures TechieRag to use the BGE-Small model if available.
    /// </summary>
    /// <param name="builder">The TechieRag builder instance.</param>
    /// <param name="modelDirectory">
    /// Optional custom model directory. Defaults to <c>&lt;ModelRoot&gt;/bge-small-en-v1.5</c>, where the
    /// files must be staged by the host (this model is not downloaded automatically).
    /// </param>
    /// <returns>The builder instance for chaining.</returns>
    public static TechieRagBuilder UseBgeSmall(
        this TechieRagBuilder builder,
        string? modelDirectory = null)
    {
        var path = modelDirectory ?? ModelRoot.GetModelDirectory("bge-small-en-v1.5");
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
