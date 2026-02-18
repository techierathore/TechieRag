using System.Runtime.CompilerServices;
using System.Text.Json;
using TechieRag;
using TechieRag.Abstractions;
using TechieRag.Embedded;
using TechieRag.Models;

namespace TechieRagWeb.Services;

/// <summary>
/// Manages the TechieRag instance lifecycle, allowing dynamic reconfiguration without app restart.
/// </summary>
/// <remarks>
/// This service wraps ITechieRag and forwards all calls to the underlying instance.
/// When configuration changes, call ReconfigureAsync() to recreate the instance with new settings.
/// </remarks>
public class TechieRagManager : ITechieRag, IDisposable
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TechieRagManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ITechieRag? _currentInstance;
    private bool _disposed;

    public TechieRagManager(
        IWebHostEnvironment environment,
        ILoggerFactory loggerFactory,
        ILogger<TechieRagManager> logger)
    {
        _environment = environment;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current ITechieRag instance, creating it if necessary.
    /// </summary>
    private async Task<ITechieRag> GetInstanceAsync()
    {
        if (_currentInstance != null)
            return _currentInstance;

        await _lock.WaitAsync();
        try
        {
            if (_currentInstance != null)
                return _currentInstance;

            _currentInstance = await CreateInstanceFromConfigAsync();
            return _currentInstance;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Recreates the TechieRag instance with current configuration.
    /// Call this after saving new settings.
    /// </summary>
    public async Task ReconfigureAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Reconfiguring TechieRag with new settings...");

            // Dispose old instance if it exists
            if (_currentInstance is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _currentInstance = null;

            // Create new instance with current config
            _currentInstance = await CreateInstanceFromConfigAsync();

            // Initialize the new instance
            await _currentInstance.InitializeAsync(cancellationToken);

            _logger.LogInformation("TechieRag reconfigured successfully");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Creates a new ITechieRag instance based on saved configuration.
    /// </summary>
    private async Task<ITechieRag> CreateInstanceFromConfigAsync()
    {
        var configFilePath = Path.Combine(_environment.ContentRootPath, "techierag-config.json");
        TechieRagConfig? savedConfig = null;

        if (File.Exists(configFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(configFilePath);
                savedConfig = JsonSerializer.Deserialize<TechieRagConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                _logger.LogInformation("Loaded configuration from {Path}", configFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load saved config, using defaults");
            }
        }

        var builder = new TechieRagBuilder();
        builder.WithLogging(_loggerFactory);

        if (savedConfig != null)
        {
            // Configure embedding based on saved settings
            switch (savedConfig.Embedding.Source)
            {
                case EmbeddingSource.Embedded:
                    builder.UseEmbedded();
                    break;
                case EmbeddingSource.Ollama:
                    builder.UseOllama(
                        savedConfig.Embedding.Endpoint ?? "http://localhost:11434",
                        savedConfig.Embedding.Model ?? "bge-m3");
                    break;
                case EmbeddingSource.LmStudio:
                    builder.UseLmStudio(savedConfig.Embedding.Endpoint ?? "http://localhost:1234");
                    break;
                case EmbeddingSource.Onnx:
                    builder.UseOnnx(savedConfig.Embedding.ModelPath ?? "");
                    break;
                case EmbeddingSource.OpenAI:
                    builder.UseOpenAI(
                        savedConfig.Embedding.ApiKey ?? "",
                        savedConfig.Embedding.Model ?? "text-embedding-3-small",
                        savedConfig.Embedding.Endpoint ?? "https://api.openai.com");
                    break;
                case EmbeddingSource.AzureOpenAI:
                    builder.UseAzureOpenAI(
                        savedConfig.Embedding.Endpoint ?? "",
                        savedConfig.Embedding.ApiKey ?? "",
                        savedConfig.Embedding.Model ?? "text-embedding-3-small");
                    break;
                case EmbeddingSource.Http:
                    builder.UseHttp(
                        savedConfig.Embedding.Endpoint ?? "http://localhost:7997",
                        savedConfig.Embedding.ApiFormat,
                        savedConfig.Embedding.Model ?? "bge-m3",
                        savedConfig.Embedding.Dimensions,
                        savedConfig.Embedding.ApiPath,
                        savedConfig.Embedding.ApiKey,
                        savedConfig.Embedding.RequestDelayMs);
                    break;
                default:
                    builder.UseEmbedded();
                    break;
            }

            // Configure vector store based on saved settings
            switch (savedConfig.VectorStore.Type)
            {
                case VectorStoreType.SqliteVec:
                    // UseVectorStore expects full connection string, not UseSqliteVec which expects just the path
                    builder.UseVectorStore(VectorStoreType.SqliteVec,
                        savedConfig.VectorStore.ConnectionString ?? "Data Source=techierag.db");
                    break;
                case VectorStoreType.PgVector:
                    builder.UsePgVector(savedConfig.VectorStore.ConnectionString ?? "");
                    break;
                case VectorStoreType.Qdrant:
                    builder.UseQdrant(
                        savedConfig.VectorStore.ConnectionString ?? "http://localhost:6334",
                        savedConfig.VectorStore.ApiKey);
                    break;
                default:
                    builder.UseSqliteVec();
                    break;
            }

            // Apply processing settings
            builder.WithChunkSize(
                savedConfig.Processing.DefaultChunkSize,
                savedConfig.Processing.DefaultChunkOverlap);

            // Configure LLM provider
            if (savedConfig.Llm.Source != LlmSource.None)
            {
                builder.UseLlm(
                    savedConfig.Llm.Source,
                    savedConfig.Llm.Endpoint,
                    savedConfig.Llm.ApiKey,
                    savedConfig.Llm.Model,
                    savedConfig.Llm.Temperature,
                    savedConfig.Llm.MaxTokens);
            }

            // Configure fallback LLM
            if (savedConfig.LlmFallback is not null && savedConfig.LlmFallback.Source != LlmSource.None)
            {
                builder.WithFallbackLlm(fb =>
                {
                    fb.Source = savedConfig.LlmFallback.Source;
                    fb.Endpoint = savedConfig.LlmFallback.Endpoint;
                    fb.ApiKey = savedConfig.LlmFallback.ApiKey;
                    fb.Model = savedConfig.LlmFallback.Model;
                    fb.Temperature = savedConfig.LlmFallback.Temperature;
                    fb.MaxTokens = savedConfig.LlmFallback.MaxTokens;
                });
            }

            // Configure usage tracking
            if (savedConfig.UsageTracking.Enabled)
            {
                builder.WithUsageTracking(tracking =>
                {
                    tracking.MaxTotalTokens = savedConfig.UsageTracking.MaxTotalTokens;
                    tracking.MaxCostUsd = savedConfig.UsageTracking.MaxCostUsd;
                    tracking.AlertThreshold = savedConfig.UsageTracking.AlertThreshold;
                    tracking.BlockOnExceeded = savedConfig.UsageTracking.BlockOnExceeded;
                });
            }

            // Configure resilience
            builder.WithResilience(r =>
            {
                r.MaxRetries = savedConfig.Resilience.MaxRetries;
                r.InitialRetryDelayMs = savedConfig.Resilience.InitialRetryDelayMs;
                r.MaxRetryDelayMs = savedConfig.Resilience.MaxRetryDelayMs;
                r.BackoffMultiplier = savedConfig.Resilience.BackoffMultiplier;
                r.HandleRateLimiting = savedConfig.Resilience.HandleRateLimiting;
                r.CircuitBreakerThreshold = savedConfig.Resilience.CircuitBreakerThreshold;
                r.CircuitBreakerRecoverySeconds = savedConfig.Resilience.CircuitBreakerRecoverySeconds;
                r.TimeoutSeconds = savedConfig.Resilience.TimeoutSeconds;
            });

            // Configure conversation memory
            builder.WithConversationMemory();
        }
        else
        {
            // Use defaults
            builder.UseEmbedded();
            builder.UseSqliteVec();
        }

        return builder.Build();
    }

    #region ITechieRag Implementation (forwarding to current instance)

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        await instance.InitializeAsync(cancellationToken);
    }

    public async Task<string> IngestAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        return await instance.IngestAsync(filePath, cancellationToken);
    }

    public async Task<string> IngestTextAsync(string text, string documentName, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        return await instance.IngestTextAsync(text, documentName, metadata, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> IngestDirectoryAsync(string directoryPath, string searchPattern = "*.*", CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        return await instance.IngestDirectoryAsync(directoryPath, searchPattern, cancellationToken);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        return await instance.SearchAsync(query, topK, documentFilter, cancellationToken);
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        await instance.DeleteDocumentAsync(documentId, cancellationToken);
    }

    public async Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        return await instance.ListDocumentsAsync(cancellationToken);
    }

    public async Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        return await instance.GetStatsAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        await instance.ClearAsync(cancellationToken);
    }

    public async Task<RagResponse> AskAsync(
        string question,
        int topK = 5,
        string? systemPrompt = null,
        string? documentFilter = null,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        return await instance.AskAsync(question, topK, systemPrompt, documentFilter, options, cancellationToken);
    }

    public async IAsyncEnumerable<string> AskStreamAsync(
        string question,
        int topK = 5,
        string? systemPrompt = null,
        string? documentFilter = null,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        await foreach (var token in instance.AskStreamAsync(question, topK, systemPrompt, documentFilter, options, cancellationToken))
        {
            yield return token;
        }
    }

    public async Task<RagResponse> ChatWithRagAsync(
        string userMessage,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        int topK = 5,
        string? systemPrompt = null,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        return await instance.ChatWithRagAsync(userMessage, conversationHistory, topK, systemPrompt, options, cancellationToken);
    }

    public async IAsyncEnumerable<string> ChatWithRagStreamAsync(
        string userMessage,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        int topK = 5,
        string? systemPrompt = null,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync();
        await foreach (var token in instance.ChatWithRagStreamAsync(userMessage, conversationHistory, topK, systemPrompt, options, cancellationToken))
        {
            yield return token;
        }
    }

    public ILlmProvider? GetLlmProvider()
    {
        var instance = GetInstanceAsync().GetAwaiter().GetResult();
        return instance.GetLlmProvider();
    }

    public ITokenTracker GetTokenTracker()
    {
        var instance = GetInstanceAsync().GetAwaiter().GetResult();
        return instance.GetTokenTracker();
    }

    public IConversationMemory? GetConversationMemory()
    {
        var instance = GetInstanceAsync().GetAwaiter().GetResult();
        return instance.GetConversationMemory();
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_currentInstance is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _lock.Dispose();
    }
}
