using Microsoft.Extensions.Logging;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Services;

namespace TechieRag;

public class TechieRagBuilder
{
    private readonly TechieRagConfig config = new();
    private Func<Abstractions.IEmbeddingProvider>? customEmbeddingProviderFactory;
    private Func<ILlmProvider>? customLlmProviderFactory;
    private Func<IPromptTemplate>? customPromptTemplateFactory;
    private Func<IReranker>? customRerankerFactory;
    private Func<IChunker>? customChunkerFactory;
    private IToolHandler? toolHandler;
    private bool useConversationMemory;

    public TechieRagBuilder UseCustomEmbeddingProvider(Func<Abstractions.IEmbeddingProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        customEmbeddingProviderFactory = factory;
        return this;
    }

    public TechieRagBuilder UseEmbedding(
        EmbeddingSource source,
        string? endpoint = null,
        string? apiKey = null,
        string? model = null,
        string? modelPath = null)
    {
        config.Embedding = new EmbeddingConfig
        {
            Source = source,
            Endpoint = endpoint,
            ApiKey = apiKey,
            Model = model ?? "bge-m3",
            ModelPath = modelPath
        };
        return this;
    }

    public TechieRagBuilder UseOllama(string endpoint = "http://localhost:11434", string model = "bge-m3")
        => UseEmbedding(EmbeddingSource.Ollama, endpoint, model: model);

    public TechieRagBuilder UseLmStudio(string endpoint = "http://localhost:1234")
        => UseEmbedding(EmbeddingSource.LmStudio, endpoint);

    public TechieRagBuilder UseOnnx(string modelPath)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        return UseEmbedding(EmbeddingSource.Onnx, modelPath: modelPath);
    }

    public TechieRagBuilder UseAzureOpenAI(string endpoint, string apiKey, string model = "text-embedding-3-small")
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(apiKey);
        return UseEmbedding(EmbeddingSource.AzureOpenAI, endpoint, apiKey, model);
    }

    public TechieRagBuilder UseOpenAI(string apiKey, string model = "text-embedding-3-small", string endpoint = "https://api.openai.com")
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        return UseEmbedding(EmbeddingSource.OpenAI, endpoint, apiKey, model);
    }

    /// <summary>
    /// Configures the builder to use a generic HTTP embedding service.
    /// </summary>
    /// <param name="endpoint">Base URL of the embedding service (e.g., http://localhost:7997).</param>
    /// <param name="apiFormat">API format to use (OpenAI, Ollama, or Simple). Default: OpenAI.</param>
    /// <param name="model">Model name to send in requests. Default: bge-m3.</param>
    /// <param name="dimensions">Vector dimensions. Default: 1024 for BGE-M3.</param>
    /// <param name="apiPath">Custom API path. If null, uses format default.</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para><b>Use Cases:</b></para>
    /// <list type="bullet">
    /// <item><description>ONNX models deployed in Docker containers</description></item>
    /// <item><description>TechieRag.Embedded exposed as a web service</description></item>
    /// <item><description>Any OpenAI-compatible embedding API</description></item>
    /// </list>
    /// <para><b>Examples:</b></para>
    /// <code>
    /// // ONNX container with OpenAI-compatible API
    /// builder.UseHttp("http://localhost:7997");
    ///
    /// // Custom path
    /// builder.UseHttp("http://localhost:7997", apiPath: "/api/embed");
    ///
    /// // Ollama-compatible format
    /// builder.UseHttp("http://localhost:11434", HttpApiFormat.Ollama);
    /// </code>
    /// </remarks>
    public TechieRagBuilder UseHttp(
        string endpoint,
        HttpApiFormat apiFormat = HttpApiFormat.OpenAI,
        string model = "bge-m3",
        int dimensions = 1024,
        string? apiPath = null,
        string? apiKey = null,
        int requestDelayMs = 100)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        config.Embedding = new EmbeddingConfig
        {
            Source = EmbeddingSource.Http,
            Endpoint = endpoint,
            ApiKey = apiKey,
            Model = model,
            Dimensions = dimensions,
            ApiFormat = apiFormat,
            ApiPath = apiPath,
            RequestDelayMs = requestDelayMs
        };
        return this;
    }

    public TechieRagBuilder UseVectorStore(VectorStoreType type, string connectionString, string? apiKey = null)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        config.VectorStore = new VectorStoreConfig
        {
            Type = type,
            ConnectionString = connectionString,
            ApiKey = apiKey
        };
        return this;
    }

    public TechieRagBuilder UseSqliteVec(string databasePath = "techierag.db")
        => UseVectorStore(VectorStoreType.SqliteVec, $"Data Source={databasePath}");

    public TechieRagBuilder UsePgVector(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        return UseVectorStore(VectorStoreType.PgVector, connectionString);
    }

    public TechieRagBuilder UseQdrant(string endpoint = "http://localhost:6334", string? apiKey = null)
        => UseVectorStore(VectorStoreType.Qdrant, endpoint, apiKey);

    public TechieRagBuilder WithChunkSize(int size, int overlap = 50)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);
        config.Processing.DefaultChunkSize = size;
        config.Processing.DefaultChunkOverlap = overlap;
        return this;
    }

    public TechieRagBuilder WithLogging(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        config.LoggerFactory = loggerFactory;
        return this;
    }

    public TechieRagBuilder WithTelemetry(bool enabled = true)
    {
        config.EnableTelemetry = enabled;
        return this;
    }

    // === NEW: LLM Provider Configuration ===

    /// <summary>
    /// Configures the LLM provider with full control over all settings.
    /// </summary>
    public TechieRagBuilder UseLlm(LlmSource source, string? endpoint = null, string? apiKey = null,
        string? model = null, float temperature = 0.7f, int maxTokens = 2048)
    {
        config.Llm = new LlmConfig
        {
            Source = source,
            Endpoint = endpoint,
            ApiKey = apiKey,
            Model = model ?? string.Empty,
            Temperature = temperature,
            MaxTokens = maxTokens
        };
        return this;
    }

    /// <summary>
    /// Configures Ollama as the LLM provider.
    /// </summary>
    public TechieRagBuilder UseOllamaLlm(string endpoint = "http://localhost:11434", string model = "llama3.2")
        => UseLlm(LlmSource.Ollama, endpoint, model: model);

    /// <summary>
    /// Configures LM Studio as the LLM provider.
    /// </summary>
    public TechieRagBuilder UseLmStudioLlm(string endpoint = "http://localhost:1234", string? model = null)
        => UseLlm(LlmSource.LmStudio, endpoint, model: model ?? "default");

    /// <summary>
    /// Configures an OpenAI-compatible REST API as the LLM provider.
    /// </summary>
    public TechieRagBuilder UseOpenAICompatibleLlm(string endpoint, string apiKey, string model = "gpt-4o")
        => UseLlm(LlmSource.OpenAICompatible, endpoint, apiKey, model);

    /// <summary>
    /// Configures Azure AI Foundry as the LLM provider.
    /// </summary>
    public TechieRagBuilder UseAzureAIFoundryLlm(string endpoint, string apiKey, string model,
        string apiVersion = "2024-12-01-preview")
    {
        config.Llm = new LlmConfig
        {
            Source = LlmSource.AzureAIFoundry,
            Endpoint = endpoint,
            ApiKey = apiKey,
            Model = model,
            ApiVersion = apiVersion
        };
        return this;
    }

    /// <summary>
    /// Configures Google Gemini as the LLM provider.
    /// </summary>
    public TechieRagBuilder UseGeminiLlm(string apiKey, string model = "gemini-2.0-flash")
        => UseLlm(LlmSource.GoogleGemini, "https://generativelanguage.googleapis.com", apiKey, model);

    /// <summary>
    /// Configures Anthropic Claude as the LLM provider.
    /// </summary>
    public TechieRagBuilder UseAnthropicLlm(string apiKey, string model = "claude-sonnet-4-5-20250929")
        => UseLlm(LlmSource.Anthropic, "https://api.anthropic.com", apiKey, model);

    /// <summary>
    /// Configures a custom LLM provider implementation.
    /// </summary>
    public TechieRagBuilder UseCustomLlmProvider(Func<ILlmProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        customLlmProviderFactory = factory;
        return this;
    }

    /// <summary>
    /// Configures a fallback LLM provider that activates when the primary provider fails.
    /// </summary>
    public TechieRagBuilder WithFallbackLlm(Action<LlmConfig> configure)
    {
        config.LlmFallback = new LlmConfig();
        configure(config.LlmFallback);
        return this;
    }

    /// <summary>
    /// Configures token usage tracking and budgets.
    /// </summary>
    public TechieRagBuilder WithUsageTracking(Action<UsageTrackingConfig>? configure = null)
    {
        config.UsageTracking.Enabled = true;
        configure?.Invoke(config.UsageTracking);
        return this;
    }

    /// <summary>
    /// Enables optional conversation memory for multi-turn chat.
    /// </summary>
    public TechieRagBuilder WithConversationMemory()
    {
        useConversationMemory = true;
        return this;
    }

    /// <summary>
    /// Configures the RAG prompt template.
    /// </summary>
    public TechieRagBuilder WithPromptTemplate(string? systemPrompt = null, string? contextTemplate = null)
    {
        if (systemPrompt != null) config.Prompt.SystemPrompt = systemPrompt;
        if (contextTemplate != null) config.Prompt.ContextChunkTemplate = contextTemplate;
        return this;
    }

    /// <summary>
    /// Provides a custom IPromptTemplate implementation.
    /// </summary>
    public TechieRagBuilder WithCustomPromptTemplate(Func<IPromptTemplate> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        customPromptTemplateFactory = factory;
        return this;
    }

    /// <summary>
    /// Configures retry and resilience behavior for LLM calls.
    /// </summary>
    public TechieRagBuilder WithResilience(Action<ResilienceConfig>? configure = null)
    {
        configure?.Invoke(config.Resilience);
        return this;
    }

    /// <summary>
    /// Registers a tool handler for function calling with the agent loop.
    /// </summary>
    public TechieRagBuilder WithToolHandler(IToolHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        toolHandler = handler;
        return this;
    }

    /// <summary>
    /// Registers individual tool functions for the agent loop.
    /// </summary>
    public TechieRagBuilder WithTools(Action<ToolRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var registry = new ToolRegistry();
        configure(registry);
        toolHandler = registry;
        return this;
    }

    // === NEW: Reranking, chunking, persistence, and pricing configuration ===

    /// <summary>
    /// Enables the rerank stage using a built-in API reranker (Cohere or Jina).
    /// </summary>
    /// <param name="source">The reranker source (Cohere or Jina).</param>
    /// <param name="apiKey">API key for the rerank service.</param>
    /// <param name="model">Optional model name override (defaults per source).</param>
    /// <param name="endpoint">Optional endpoint override.</param>
    /// <param name="topN">How many results the reranker returns (0 = same as requested topK).</param>
    /// <param name="candidateCount">How many vector search candidates are fetched for reranking.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public TechieRagBuilder WithReranker(
        RerankSource source,
        string apiKey,
        string? model = null,
        string? endpoint = null,
        int topN = 0,
        int candidateCount = 20)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        config.Rerank = new RerankConfig
        {
            Enabled = true,
            Source = source,
            ApiKey = apiKey,
            Model = model,
            Endpoint = endpoint,
            TopN = topN,
            CandidateCount = candidateCount
        };
        return this;
    }

    /// <summary>
    /// Enables the rerank stage using a custom IReranker implementation
    /// (e.g. the local ONNX cross-encoder from the TechieRag.Embedded package).
    /// </summary>
    /// <param name="factory">Factory that creates the reranker.</param>
    /// <param name="topN">How many results the reranker returns (0 = same as requested topK).</param>
    /// <param name="candidateCount">How many vector search candidates are fetched for reranking.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public TechieRagBuilder WithReranker(Func<IReranker> factory, int topN = 0, int candidateCount = 20)
    {
        ArgumentNullException.ThrowIfNull(factory);
        customRerankerFactory = factory;
        config.Rerank.Enabled = true;
        config.Rerank.Source = RerankSource.Custom;
        config.Rerank.TopN = topN;
        config.Rerank.CandidateCount = candidateCount;
        return this;
    }

    /// <summary>
    /// Selects the chunking strategy used during ingestion.
    /// </summary>
    /// <param name="strategy">The chunking strategy (Recursive, Token, Markdown, or Sentence).</param>
    /// <returns>The builder instance for method chaining.</returns>
    public TechieRagBuilder WithChunking(ChunkingStrategy strategy)
    {
        config.Processing.ChunkingStrategy = strategy;
        return this;
    }

    /// <summary>
    /// Provides a custom IChunker implementation used during ingestion.
    /// </summary>
    /// <param name="factory">Factory that creates the chunker.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public TechieRagBuilder WithCustomChunker(Func<IChunker> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        customChunkerFactory = factory;
        return this;
    }

    /// <summary>
    /// Enables relational persistence for conversation threads and workspaces
    /// (TrThread, TrMessage, TrWorkspace, TrWorkspaceDocument tables — self-created).
    /// </summary>
    /// <param name="provider">The persistence provider (Sqlite or Postgres).</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="defaultUserId">Default user for persistent conversation memory.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public TechieRagBuilder WithPersistence(StoreProvider provider, string connectionString, string defaultUserId = "default")
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        config.Persistence = new PersistenceConfig
        {
            Provider = provider,
            ConnectionString = connectionString,
            DefaultUserId = defaultUserId
        };
        return this;
    }

    /// <summary>
    /// Sets or overrides the cost-estimation pricing for a model.
    /// </summary>
    /// <param name="modelName">The model name (matched case-insensitively and by substring).</param>
    /// <param name="inputPerMillionUsd">Input token price per one million tokens in USD.</param>
    /// <param name="outputPerMillionUsd">Output token price per one million tokens in USD.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public TechieRagBuilder WithModelPricing(string modelName, decimal inputPerMillionUsd, decimal outputPerMillionUsd)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        config.UsageTracking.Pricing[modelName] = new Models.ModelPricing
        {
            InputPerMillionUsd = inputPerMillionUsd,
            OutputPerMillionUsd = outputPerMillionUsd
        };
        return this;
    }

    public ITechieRag Build()
    {
        var vectorStore = CreateVectorStore();
        var embeddingProvider = CreateEmbeddingProvider();
        var processors = CreateProcessors();
        var logger = config.LoggerFactory?.CreateLogger<TechieRagClient>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TechieRagClient>.Instance;

        // Create LLM provider (if configured)
        ILlmProvider? llmProvider = null;
        if (config.Llm.Source != LlmSource.None || customLlmProviderFactory != null)
        {
            llmProvider = CreateLlmProvider();

            // Wrap with retry handler
            llmProvider = new RetryHandler(llmProvider, config.Resilience,
                config.LoggerFactory?.CreateLogger<RetryHandler>());

            // Wrap with fallback (if configured)
            if (config.LlmFallback is not null && config.LlmFallback.Source != LlmSource.None)
            {
                var fallbackProvider = CreateLlmProviderFromConfig(config.LlmFallback);
                llmProvider = new FallbackLlmHandler(llmProvider, fallbackProvider,
                    config.LoggerFactory?.CreateLogger<FallbackLlmHandler>());
            }
        }

        // Create token tracker
        ITokenTracker? tokenTracker = null;
        if (config.UsageTracking.Enabled)
        {
            tokenTracker = new TokenUsageTracker(config.UsageTracking);
            if (llmProvider != null)
            {
                llmProvider.OnCompletionCompleted += (_, args) => tokenTracker.RecordUsage(new Models.TokenUsage
                {
                    InputTokens = args.InputTokens,
                    OutputTokens = args.OutputTokens,
                    ModelName = args.ModelName,
                    ProviderName = args.ProviderName
                });
            }
        }

        // Create persistence stores (if configured)
        IConversationStore? conversationStore = CreateConversationStore();
        IWorkspaceStore? workspaceStore = CreateWorkspaceStore();

        // Create conversation memory (persistent when a conversation store is available)
        IConversationMemory? conversationMemory = useConversationMemory
            ? conversationStore is not null
                ? new DbConversationMemory(conversationStore, config.Persistence.DefaultUserId)
                : new InMemoryConversationMemory()
            : null;

        // Create prompt template
        IPromptTemplate promptTemplate = customPromptTemplateFactory?.Invoke() ?? new PromptTemplateEngine(config.Prompt);

        // Create reranker (if configured)
        var reranker = CreateReranker();

        // Create chunker
        var chunker = customChunkerFactory?.Invoke() ?? CreateChunkerFromStrategy(config.Processing.ChunkingStrategy);

        return new TechieRagClient(vectorStore, embeddingProvider, processors, config, logger,
            llmProvider, tokenTracker, conversationMemory, promptTemplate,
            reranker, chunker, conversationStore, workspaceStore);
    }

    /// <summary>
    /// Creates the configured reranker, or null when reranking is not configured.
    /// </summary>
    internal IReranker? CreateReranker()
    {
        if (customRerankerFactory is not null)
            return customRerankerFactory();

        if (!config.Rerank.Enabled || config.Rerank.Source is RerankSource.None or RerankSource.Custom)
            return null;

        return config.Rerank.Source switch
        {
            RerankSource.Cohere => new Reranking.CohereReranker(
                config.Rerank.ApiKey ?? throw new InvalidOperationException("ApiKey is required for the Cohere reranker."),
                config.Rerank.Model ?? "rerank-v3.5",
                config.Rerank.Endpoint,
                config.LoggerFactory?.CreateLogger<Reranking.CohereReranker>()),

            RerankSource.Jina => new Reranking.JinaReranker(
                config.Rerank.ApiKey ?? throw new InvalidOperationException("ApiKey is required for the Jina reranker."),
                config.Rerank.Model ?? "jina-reranker-v2-base-multilingual",
                config.Rerank.Endpoint,
                config.LoggerFactory?.CreateLogger<Reranking.JinaReranker>()),

            RerankSource.LocalOnnx => throw new InvalidOperationException(
                "RerankSource.LocalOnnx requires the TechieRag.Embedded package. " +
                "Install the package and use .WithReranker(() => new OnnxCrossEncoderReranker(...))."),

            _ => throw new InvalidOperationException($"Unsupported reranker source: {config.Rerank.Source}")
        };
    }

    /// <summary>
    /// Creates the chunker for the given strategy.
    /// </summary>
    internal static IChunker CreateChunkerFromStrategy(ChunkingStrategy strategy) => strategy switch
    {
        ChunkingStrategy.Recursive => Processors.Chunking.RecursiveChunker.Instance,
        ChunkingStrategy.Token => new Processors.Chunking.TokenChunker(),
        ChunkingStrategy.Markdown => new Processors.Chunking.MarkdownChunker(),
        ChunkingStrategy.Sentence => new Processors.Chunking.SentenceChunker(),
        _ => Processors.Chunking.RecursiveChunker.Instance
    };

    private IConversationStore? CreateConversationStore() => config.Persistence.Provider switch
    {
        StoreProvider.None => null,
        StoreProvider.Sqlite => new Persistence.SqliteConversationStore(RequirePersistenceConnectionString()),
        StoreProvider.Postgres => new Persistence.PostgresConversationStore(RequirePersistenceConnectionString()),
        _ => throw new InvalidOperationException($"Unsupported persistence provider: {config.Persistence.Provider}")
    };

    private IWorkspaceStore? CreateWorkspaceStore() => config.Persistence.Provider switch
    {
        StoreProvider.None => null,
        StoreProvider.Sqlite => new Persistence.SqliteWorkspaceStore(RequirePersistenceConnectionString()),
        StoreProvider.Postgres => new Persistence.PostgresWorkspaceStore(RequirePersistenceConnectionString()),
        _ => throw new InvalidOperationException($"Unsupported persistence provider: {config.Persistence.Provider}")
    };

    private string RequirePersistenceConnectionString() =>
        config.Persistence.ConnectionString
        ?? throw new InvalidOperationException("Persistence.ConnectionString is required when a persistence provider is configured.");

    /// <summary>
    /// Creates the configured LLM provider.
    /// </summary>
    internal ILlmProvider CreateLlmProvider()
    {
        if (customLlmProviderFactory != null)
            return customLlmProviderFactory();

        return CreateLlmProviderFromConfig(config.Llm);
    }

    /// <summary>
    /// Creates an LLM provider from the given config (used for primary and fallback).
    /// </summary>
    internal ILlmProvider CreateLlmProviderFromConfig(LlmConfig llmConfig)
    {
        return llmConfig.Source switch
        {
            LlmSource.Ollama => new OllamaLlmProvider(
                llmConfig.Endpoint ?? "http://localhost:11434",
                llmConfig.Model,
                config.LoggerFactory?.CreateLogger<OllamaLlmProvider>()),

            LlmSource.LmStudio => new LmStudioLlmProvider(
                llmConfig.Endpoint ?? "http://localhost:1234",
                llmConfig.Model,
                config.LoggerFactory?.CreateLogger<LmStudioLlmProvider>()),

            LlmSource.OpenAICompatible => new OpenAICompatibleLlmProvider(
                llmConfig.Endpoint ?? throw new InvalidOperationException("Endpoint is required for OpenAI-compatible LLM provider."),
                llmConfig.ApiKey ?? string.Empty,
                llmConfig.Model,
                config.LoggerFactory?.CreateLogger<OpenAICompatibleLlmProvider>()),

            LlmSource.AzureAIFoundry => new AzureAIFoundryLlmProvider(
                llmConfig.Endpoint ?? throw new InvalidOperationException("Endpoint is required for Azure AI Foundry LLM provider."),
                llmConfig.ApiKey ?? throw new InvalidOperationException("ApiKey is required for Azure AI Foundry LLM provider."),
                llmConfig.Model,
                llmConfig.ApiVersion ?? "2024-12-01-preview",
                config.LoggerFactory?.CreateLogger<AzureAIFoundryLlmProvider>()),

            LlmSource.GoogleGemini => new GoogleGeminiLlmProvider(
                llmConfig.ApiKey ?? throw new InvalidOperationException("ApiKey is required for Google Gemini LLM provider."),
                llmConfig.Model,
                llmConfig.Endpoint,
                config.LoggerFactory?.CreateLogger<GoogleGeminiLlmProvider>()),

            LlmSource.Anthropic => new AnthropicLlmProvider(
                llmConfig.ApiKey ?? throw new InvalidOperationException("ApiKey is required for Anthropic LLM provider."),
                llmConfig.Model,
                llmConfig.Endpoint,
                llmConfig.MaxTokens,
                config.LoggerFactory?.CreateLogger<AnthropicLlmProvider>()),

            _ => throw new InvalidOperationException($"Unsupported LLM source: {llmConfig.Source}")
        };
    }

    private Abstractions.IVectorStore CreateVectorStore()
    {
        return config.VectorStore.Type switch
        {
            VectorStoreType.SqliteVec => new VectorStores.SqliteVecStore(config.VectorStore.ConnectionString),
            VectorStoreType.PgVector => CreatePgVectorStore(),
            VectorStoreType.Qdrant => new VectorStores.QdrantStore(config.VectorStore.ConnectionString, apiKey: config.VectorStore.ApiKey),
            _ => throw new InvalidOperationException($"Unsupported vector store type: {config.VectorStore.Type}")
        };
    }

    private VectorStores.PgVectorStore CreatePgVectorStore()
    {
        var pgLogger = config.LoggerFactory?.CreateLogger<VectorStores.PgVectorStore>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<VectorStores.PgVectorStore>.Instance;
        return new VectorStores.PgVectorStore(config.VectorStore.ConnectionString, pgLogger);
    }

    private Abstractions.IEmbeddingProvider CreateEmbeddingProvider()
    {
        if (customEmbeddingProviderFactory != null)
        {
            return customEmbeddingProviderFactory();
        }

        return config.Embedding.Source switch
        {
            EmbeddingSource.Embedded => throw new InvalidOperationException(
                "EmbeddingSource.Embedded requires the TechieRag.Embedded package. " +
                "Install the package and use .UseEmbedded() instead of .UseEmbedding(EmbeddingSource.Embedded, ...)."),
            EmbeddingSource.Ollama => CreateOllamaProvider(),
            EmbeddingSource.LmStudio => CreateLmStudioProvider(),
            EmbeddingSource.Onnx => CreateOnnxProvider(),
            EmbeddingSource.AzureOpenAI => CreateAzureOpenAIProvider(),
            EmbeddingSource.OpenAI => CreateOpenAIProvider(),
            EmbeddingSource.Http => CreateHttpProvider(),
            _ => throw new InvalidOperationException($"Unsupported embedding source: {config.Embedding.Source}")
        };
    }

    private Embedding.OllamaEmbeddingProvider CreateOllamaProvider()
    {
        var endpoint = config.Embedding.Endpoint ?? "http://localhost:11434";
        return new Embedding.OllamaEmbeddingProvider(endpoint, config.Embedding.Model);
    }

    private Embedding.LmStudioEmbeddingProvider CreateLmStudioProvider()
    {
        var endpoint = config.Embedding.Endpoint ?? "http://localhost:1234";
        return new Embedding.LmStudioEmbeddingProvider(endpoint, config.Embedding.Model);
    }

    private Embedding.OnnxEmbeddingProvider CreateOnnxProvider()
    {
        if (string.IsNullOrEmpty(config.Embedding.ModelPath))
        {
            throw new InvalidOperationException("ModelPath is required for ONNX embedding provider.");
        }
        return new Embedding.OnnxEmbeddingProvider(config.Embedding.ModelPath, config.Embedding.Model);
    }

    private Embedding.AzureOpenAIEmbeddingProvider CreateAzureOpenAIProvider()
    {
        if (string.IsNullOrEmpty(config.Embedding.Endpoint))
        {
            throw new InvalidOperationException("Endpoint is required for Azure OpenAI embedding provider.");
        }
        if (string.IsNullOrEmpty(config.Embedding.ApiKey))
        {
            throw new InvalidOperationException("ApiKey is required for Azure OpenAI embedding provider.");
        }
        return new Embedding.AzureOpenAIEmbeddingProvider(
            config.Embedding.Endpoint,
            config.Embedding.ApiKey,
            config.Embedding.Model);
    }

    private Embedding.AzureOpenAIEmbeddingProvider CreateOpenAIProvider()
    {
        if (string.IsNullOrEmpty(config.Embedding.ApiKey))
        {
            throw new InvalidOperationException("ApiKey is required for OpenAI embedding provider.");
        }
        var endpoint = config.Embedding.Endpoint ?? "https://api.openai.com";
        return new Embedding.AzureOpenAIEmbeddingProvider(
            endpoint,
            config.Embedding.ApiKey,
            config.Embedding.Model);
    }

    private Embedding.HttpEmbeddingProvider CreateHttpProvider()
    {
        if (string.IsNullOrEmpty(config.Embedding.Endpoint))
        {
            throw new InvalidOperationException("Endpoint is required for HTTP embedding provider.");
        }
        return new Embedding.HttpEmbeddingProvider(
            config.Embedding.Endpoint,
            config.Embedding.ApiFormat,
            config.Embedding.Model,
            config.Embedding.Dimensions,
            config.Embedding.ApiPath,
            config.Embedding.ApiKey,
            timeoutSeconds: 60,
            requestDelayMs: config.Embedding.RequestDelayMs > 0 ? config.Embedding.RequestDelayMs : 200);
    }

    private IEnumerable<Abstractions.IDocumentProcessor> CreateProcessors()
    {
        return new Abstractions.IDocumentProcessor[]
        {
            new Processors.TextProcessor(),
            new Processors.MarkdownProcessor(),
            new Processors.PdfProcessor(),
            new Processors.DocxProcessor(),
            new Processors.XlsxProcessor(),
            new Processors.PptxProcessor(),
            new Processors.CsvProcessor(),
            new Processors.HtmlProcessor(),
            new Processors.JsonProcessor(),
            new Processors.TomlProcessor(),
            new Processors.CodeProcessor(),
            // GenericTextProcessor must be last - it's the fallback for unknown text-based files
            new Processors.GenericTextProcessor()
        };
    }

    /// <summary>Gets the tool handler if one was configured.</summary>
    internal IToolHandler? GetToolHandler() => toolHandler;

    /// <summary>Gets whether conversation memory was requested.</summary>
    internal bool UseConversationMemory => useConversationMemory;

    public TechieRagConfig GetConfig() => config;
}
