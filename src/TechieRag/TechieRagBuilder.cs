using Microsoft.Extensions.Logging;

namespace TechieRag;

public class TechieRagBuilder
{
    private readonly TechieRagConfig config = new();
    private Func<Abstractions.IEmbeddingProvider>? customEmbeddingProviderFactory;

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

    public TechieRagBuilder UseVectorStore(VectorStoreType type, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        config.VectorStore = new VectorStoreConfig
        {
            Type = type,
            ConnectionString = connectionString
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

    public TechieRagBuilder UseQdrant(string endpoint = "http://localhost:6334")
        => UseVectorStore(VectorStoreType.Qdrant, endpoint);

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

    public ITechieRag Build()
    {
        var vectorStore = CreateVectorStore();
        var embeddingProvider = CreateEmbeddingProvider();
        var processors = CreateProcessors();
        var logger = config.LoggerFactory?.CreateLogger<TechieRagClient>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TechieRagClient>.Instance;

        return new TechieRagClient(vectorStore, embeddingProvider, processors, config, logger);
    }

    private Abstractions.IVectorStore CreateVectorStore()
    {
        return config.VectorStore.Type switch
        {
            VectorStoreType.SqliteVec => new VectorStores.SqliteVecStore(config.VectorStore.ConnectionString),
            VectorStoreType.PgVector => CreatePgVectorStore(),
            VectorStoreType.Qdrant => new VectorStores.QdrantStore(config.VectorStore.ConnectionString),
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
            new Processors.HtmlProcessor(),
            new Processors.JsonProcessor(),
            new Processors.TomlProcessor(),
            new Processors.CodeProcessor(),
            // GenericTextProcessor must be last - it's the fallback for unknown text-based files
            new Processors.GenericTextProcessor()
        };
    }

    public TechieRagConfig GetConfig() => config;
}
