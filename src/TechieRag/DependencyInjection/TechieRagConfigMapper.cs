namespace TechieRag.DependencyInjection;

/// <summary>
/// Copies every field of a <see cref="TechieRagConfig"/> onto a <see cref="TechieRagBuilder"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-FN-066 / BRD-159. Both <c>AddTechieRag(IConfiguration)</c> and
/// <c>AddTechieRag(TechieRagConfig)</c> go through this one method, so a field set in appsettings and a
/// field set on a config object reach the built instance the same way. Before it, the two overloads
/// each hand-picked a subset: <c>VectorStore.ApiKey</c>, the embedding <c>Dimensions</c>,
/// <c>ApiFormat</c>, <c>ApiPath</c>, <c>RequestDelayMs</c> and the whole <c>Prompt</c> section were
/// silently dropped.</para>
/// <para><b>Guard:</b> <c>TechieRagConfigMappingTests</c> walks every public settable property of the
/// configuration tree by reflection and fails if any of them does not survive the mapping, so a field
/// added later cannot be missed.</para>
/// <para><b>Kept from the old overloads:</b> a rerank or persistence section that configuration alone
/// cannot make usable (an API reranker without its key, the local ONNX reranker whose factory lives in
/// TechieRag.Embedded, a persistence provider without a connection string) leaves that stage off
/// instead of throwing when <see cref="ITechieRag"/> is resolved.</para>
/// </remarks>
internal static class TechieRagConfigMapper
{
    /// <summary>
    /// Applies every field of <paramref name="source"/> to <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="source">The configuration to copy from.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    internal static void Apply(TechieRagBuilder builder, TechieRagConfig source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);

        MapEmbedding(builder, source.Embedding);
        builder.UseVectorStore(source.VectorStore.Type, source.VectorStore.ConnectionString, source.VectorStore.ApiKey);
        builder.WithChunkSize(source.Processing.DefaultChunkSize, source.Processing.DefaultChunkOverlap);
        builder.WithChunking(source.Processing.ChunkingStrategy);
        builder.WithTelemetry(source.EnableTelemetry);
        MapLlm(builder, source);
        MapUsageTracking(builder.GetConfig().UsageTracking, source.UsageTracking);
        MapPrompt(builder, source.Prompt);
        builder.WithResilience(target => CopyResilience(source.Resilience, target));
        MapRerank(builder.GetConfig().Rerank, source.Rerank);
        MapPersistence(builder, source.Persistence);
    }

    private static void MapEmbedding(TechieRagBuilder builder, EmbeddingConfig source)
    {
        builder.UseEmbedding(source.Source, source.Endpoint, source.ApiKey, source.Model, source.ModelPath);

        var target = builder.GetConfig().Embedding;
        target.ApiFormat = source.ApiFormat;
        target.ApiPath = source.ApiPath;
        target.Dimensions = source.Dimensions;
        target.RequestDelayMs = source.RequestDelayMs;
    }

    private static void MapLlm(TechieRagBuilder builder, TechieRagConfig source)
    {
        builder.UseLlm(source.Llm.Source);
        CopyLlm(source.Llm, builder.GetConfig().Llm);

        if (source.LlmFallback is not null)
        {
            builder.WithFallbackLlm(target => CopyLlm(source.LlmFallback, target));
        }
    }

    private static void CopyLlm(LlmConfig source, LlmConfig target)
    {
        target.Source = source.Source;
        target.Endpoint = source.Endpoint;
        target.ApiKey = source.ApiKey;
        target.Model = source.Model;
        target.Temperature = source.Temperature;
        target.MaxTokens = source.MaxTokens;
        target.ApiVersion = source.ApiVersion;
        target.ProjectId = source.ProjectId;
        target.MaxContextTokens = source.MaxContextTokens;
        target.Connector = source.Connector;
    }

    private static void MapUsageTracking(UsageTrackingConfig target, UsageTrackingConfig source)
    {
        target.Enabled = source.Enabled;
        target.MaxTotalTokens = source.MaxTotalTokens;
        target.MaxCostUsd = source.MaxCostUsd;
        target.AlertThreshold = source.AlertThreshold;
        target.BlockOnExceeded = source.BlockOnExceeded;
        target.Pricing = new Dictionary<string, Models.ModelPricing>(source.Pricing, StringComparer.OrdinalIgnoreCase);
    }

    private static void MapPrompt(TechieRagBuilder builder, PromptConfig source)
    {
        builder.WithPromptTemplate(source.SystemPrompt, source.ContextChunkTemplate);

        var target = builder.GetConfig().Prompt;
        target.MaxContextChunks = source.MaxContextChunks;
        target.MaxContextTokens = source.MaxContextTokens;
    }

    private static void CopyResilience(ResilienceConfig source, ResilienceConfig target)
    {
        target.MaxRetries = source.MaxRetries;
        target.InitialRetryDelayMs = source.InitialRetryDelayMs;
        target.MaxRetryDelayMs = source.MaxRetryDelayMs;
        target.BackoffMultiplier = source.BackoffMultiplier;
        target.HandleRateLimiting = source.HandleRateLimiting;
        target.CircuitBreakerThreshold = source.CircuitBreakerThreshold;
        target.CircuitBreakerRecoverySeconds = source.CircuitBreakerRecoverySeconds;
        target.TimeoutSeconds = source.TimeoutSeconds;
    }

    private static void MapRerank(RerankConfig target, RerankConfig source)
    {
        target.Source = source.Source;
        target.Endpoint = source.Endpoint;
        target.ApiKey = source.ApiKey;
        target.Model = source.Model;
        target.TopN = source.TopN;
        target.CandidateCount = source.CandidateCount;
        target.ModelPath = source.ModelPath;
        target.Enabled = source.Enabled && IsRerankUsableFromConfiguration(source);
    }

    /// <summary>
    /// Whether configuration alone can produce the configured reranker.
    /// </summary>
    /// <param name="source">The rerank section.</param>
    /// <returns>False for an API reranker without its key and for the local ONNX reranker, whose
    /// factory only TechieRag.Embedded can supply.</returns>
    private static bool IsRerankUsableFromConfiguration(RerankConfig source) => source.Source switch
    {
        RerankSource.Cohere or RerankSource.Jina => !string.IsNullOrEmpty(source.ApiKey),
        RerankSource.LocalOnnx => false,
        _ => true
    };

    private static void MapPersistence(TechieRagBuilder builder, PersistenceConfig source)
    {
        if (source.Provider != StoreProvider.None && !string.IsNullOrEmpty(source.ConnectionString))
        {
            builder.WithPersistence(source.Provider, source.ConnectionString, source.DefaultUserId);
            return;
        }

        builder.GetConfig().Persistence.DefaultUserId = source.DefaultUserId;
    }
}
