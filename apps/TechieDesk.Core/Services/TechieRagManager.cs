using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using TechieDesk.Services.Hosting;
using TechieRag;
using TechieRag.Abstractions;
using TechieRag.Embedded;
using TechieRag.Models;
using TechieRag.Services;

using TechieDeskDb;

namespace TechieDesk.Services;

/// <summary>
/// Manages the TechieRag instance lifecycle, allowing dynamic reconfiguration without app restart.
/// </summary>
/// <remarks>
/// <para>
/// This service wraps ITechieRag and forwards all calls to the underlying instance.
/// When configuration changes, call ReconfigureAsync() to recreate the instance with new settings.
/// </para>
/// <para>
/// <b>REQ-FN-049.</b> Every await in this type carries <c>ConfigureAwait(false)</c>, and that is not
/// cosmetic. This is library code with no affinity to any thread, but its continuations used to be
/// posted back to whichever <see cref="SynchronizationContext"/> called in. The composition root
/// blocked the UIKit launch thread on <c>InitializeAsync().GetAwaiter().GetResult()</c>, the
/// <c>File.ReadAllTextAsync</c> of <c>techierag-config.json</c> below then tried to resume on that
/// same blocked thread, and the app presented zero windows forever — on every install that had ever
/// saved provider settings, and only on those, because with no config file that await never runs.
/// The composition root no longer blocks (that is the primary fix); these annotations are what stop
/// the next caller that does from resurrecting the hang.
/// </para>
/// </remarks>
public class TechieRagManager : ITechieRag, IDisposable
{
    private readonly IAppEnvironment environment;
    private readonly IConfiguration configuration;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<TechieRagManager> logger;
    private readonly SemaphoreSlim instanceLock = new(1, 1);
    private readonly TechieRagConfigProtector configProtector;
    private ITechieRag? currentInstance;
    private bool disposed;

    /// <summary>
    /// Creates a new <see cref="TechieRagManager"/>.
    /// </summary>
    /// <param name="environment">The application environment, used to resolve the saved-config path.</param>
    /// <param name="loggerFactory">Logger factory handed to the TechieRag builder.</param>
    /// <param name="logger">Logger for lifecycle diagnostics.</param>
    /// <param name="dataProtectionProvider">Data Protection provider used to decrypt the provider
    /// API keys stored encrypted at rest in <c>techierag-config.json</c> (REQ-NFR-004).</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="secretStore">The OS credential store provider API keys are kept in (REQ-FN-039).
    /// Optional so a host without a platform store still falls back to encryption at rest; the DI
    /// container supplies it whenever one is registered.</param>
    public TechieRagManager(
        IAppEnvironment environment,
        ILoggerFactory loggerFactory,
        ILogger<TechieRagManager> logger,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        TechieDesk.Services.Auth.ISecretStore? secretStore = null)
    {
        this.environment = environment;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
        this.configuration = configuration;
        this.configProtector = new TechieRagConfigProtector(
            dataProtectionProvider,
            loggerFactory.CreateLogger<TechieRagConfigProtector>(),
            secretStore);
    }

    /// <summary>
    /// Resolves the one data directory holding every persistent artefact (REQ-FN-034).
    /// </summary>
    /// <remarks>
    /// Relocates the legacy vector database on first use. Earlier builds passed no path to
    /// <c>UseSqliteVec</c>, so the vector store landed beside the process working directory instead
    /// of under the data directory, meaning every embedding was discarded when that location was
    /// wiped. Since REQ-FN-037 the destination is the per-user OS directory; the whole legacy
    /// app-relative <c>data/</c> folder is swept once at launch by <c>MauiProgram</c>, and this
    /// handles the older still-beside-the-executable shape.
    /// </remarks>
    /// <returns>An absolute path to the existing data directory.</returns>
    private string ResolveDataDirectory()
    {
        var dataDirectory = DataDirectory.ResolveAndCreate(configuration[DataDirectory.ConfigKey]);
        var currentVectorDb = Path.Combine(dataDirectory, DataDirectory.VectorDbFileName);

        // REQ-FN-048: BOTH roots an earlier build could have resolved the relative default against.
        // The content root is the pre-REQ-FN-037 shape; the working directory is the one that was
        // missed, and it is the damaging one — exec'd from inside the bundle it wrote a live vector
        // database into TechieDesk.app's root, which codesign rejects as unsealed content. Relocated,
        // not deleted: it holds the install's embeddings.
        foreach (var legacyVectorDb in new[]
                 {
                     Path.Combine(environment.ContentRootPath, DataDirectory.VectorDbFileName),
                     Path.GetFullPath(DataDirectory.VectorDbFileName)
                 })
        {
            if (DataDirectory.RelocateLegacyArtefact(legacyVectorDb, currentVectorDb))
            {
                logger.LogInformation(
                    "Relocated the legacy vector database into the data directory (REQ-FN-034/037/048): {From} -> {To}",
                    legacyVectorDb, currentVectorDb);
            }
        }

        return dataDirectory;
    }

    /// <summary>
    /// Gets the default SqliteVec connection string, always inside the resolved data directory.
    /// </summary>
    /// <returns>A SQLite connection string for the vector database.</returns>
    private string DefaultVectorConnectionString() =>
        DataDirectory.VectorDbConnectionString(ResolveDataDirectory());

    /// <summary>
    /// Gets the absolute path of the saved configuration this manager builds the RAG instance from
    /// (REQ-FN-052).
    /// </summary>
    /// <returns>An absolute path to <c>techierag-config.json</c>.</returns>
    /// <remarks>
    /// Derived from <see cref="DataDirectory.ConfigFilePath"/> — the same helper
    /// <see cref="TechieRagConfigService"/> writes through — and used by
    /// <see cref="CreateInstanceFromConfigAsync"/> itself, so the value a test observes here is the
    /// file the running instance is genuinely built from rather than a second copy of the rule.
    /// </remarks>
    public string ResolveConfigFilePath() =>
        DataDirectory.ConfigFilePath(ResolveDataDirectory());

    /// <summary>
    /// Reads the saved configuration exactly as the instance build reads it, credentials revealed.
    /// </summary>
    /// <returns>The saved configuration, or null when no readable file exists.</returns>
    /// <remarks>
    /// Extracted from <see cref="CreateInstanceFromConfigAsync"/> so REQ-FN-052 can assert what the
    /// READ side of the round trip actually sees without standing up an embedding provider and a
    /// vector database. The instance build calls this method, so there is one read, not two.
    /// </remarks>
    public async Task<TechieRagConfig?> ReadSavedConfigAsync()
    {
        var configFilePath = ResolveConfigFilePath();
        if (!File.Exists(configFilePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configFilePath).ConfigureAwait(false);
            var savedConfig = JsonSerializer.Deserialize<TechieRagConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // REQ-NFR-004: provider API keys are stored encrypted at rest. Decrypt them before
            // handing them to the builder. Legacy cleartext values are used as-is; rewriting the
            // file is TechieRagConfigService's job, not this read-only consumer's.
            if (savedConfig != null)
            {
                configProtector.RevealSecrets(savedConfig);
            }

            logger.LogInformation("Loaded configuration from {Path}", configFilePath);
            return savedConfig;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load saved config, using defaults");
            return null;
        }
    }

    /// <summary>
    /// Resolves the LLM provider the running instance is built with, straight from the saved file
    /// (REQ-FN-052).
    /// </summary>
    /// <returns>
    /// The provider configuration handed to the builder, or null when none is configured or the saved
    /// one is not buildable.
    /// </returns>
    /// <remarks>
    /// This is the read half of the REQ-FN-052 round trip: LLM Settings saves through
    /// <see cref="TechieRagConfigService"/>, and this reports what the RAG instance resolves from that
    /// same file with no restart. <see cref="CreateInstanceFromConfigAsync"/> applies the identical
    /// decision through <see cref="SelectBuildableLlm"/>, so a test asserting on this is asserting on
    /// the production read path.
    /// </remarks>
    public async Task<LlmConfig?> ResolveConfiguredLlmAsync()
    {
        var savedConfig = await ReadSavedConfigAsync().ConfigureAwait(false);
        return savedConfig is null ? null : SelectBuildableLlm(savedConfig.Llm, "primary");
    }

    /// <summary>
    /// Decides whether a saved provider may be handed to the builder (REQ-UI-043).
    /// </summary>
    /// <param name="llm">The saved provider configuration, primary or fallback.</param>
    /// <param name="role">Which slot it fills, used only for the diagnostic message.</param>
    /// <returns>The provider to build, or null when there is none or it is unusable.</returns>
    /// <remarks>
    /// REQ-UI-043 / BRD-136: a configuration written by an older build (or edited by hand) can still
    /// be half-configured on disk. Handing it to the builder throws "Endpoint is required for ..." out
    /// of every page that touches TechieRag, including pages with nothing to do with chat. The
    /// unbuildable provider is skipped and logged instead, so the rest of the app — retrieval,
    /// /token-usage, admin — stays up.
    /// </remarks>
    private LlmConfig? SelectBuildableLlm(LlmConfig? llm, string role)
    {
        if (llm is null || llm.Source == LlmSource.None)
        {
            return null;
        }

        if (LlmConfigValidator.IsBuildable(llm))
        {
            return llm;
        }

        logger.LogWarning(
            "Skipping the saved {Source} {Role} LLM provider because it is not fully configured "
            + "(REQ-UI-043): {Errors}",
            llm.Source,
            role,
            string.Join(" ", LlmConfigValidator.Validate(llm).Select(e => e.Describe())));
        return null;
    }

    /// <summary>
    /// Gets the current ITechieRag instance, creating it if necessary.
    /// </summary>
    private async Task<ITechieRag> GetInstanceAsync()
    {
        if (currentInstance != null)
            return currentInstance;

        await instanceLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (currentInstance != null)
                return currentInstance;

            currentInstance = await CreateInstanceFromConfigAsync().ConfigureAwait(false);
            return currentInstance;
        }
        finally
        {
            instanceLock.Release();
        }
    }

    /// <summary>
    /// Recreates the TechieRag instance with current configuration.
    /// Call this after saving new settings.
    /// </summary>
    public async Task ReconfigureAsync(CancellationToken cancellationToken = default)
    {
        await instanceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            logger.LogInformation("Reconfiguring TechieRag with new settings...");

            // Dispose old instance if it exists
            if (currentInstance is IDisposable disposable)
            {
                disposable.Dispose();
            }
            currentInstance = null;

            // Create new instance with current config
            currentInstance = await CreateInstanceFromConfigAsync().ConfigureAwait(false);

            // Initialize the new instance
            await currentInstance.InitializeAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation("TechieRag reconfigured successfully");
        }
        finally
        {
            instanceLock.Release();
        }
    }

    /// <summary>
    /// Creates a new ITechieRag instance based on saved configuration.
    /// </summary>
    private async Task<ITechieRag> CreateInstanceFromConfigAsync()
    {
        // REQ-FN-034 / REQ-FN-052: read the saved config from the ONE data directory, through the ONE
        // path helper the writer also uses. This used to resolve against the content root
        // independently of TechieRagConfigService, so the writer and this reader could disagree about
        // which file held the provider settings.
        var savedConfig = await ReadSavedConfigAsync().ConfigureAwait(false);

        var builder = new TechieRagBuilder();
        builder.WithLogging(loggerFactory);

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
                    // UseVectorStore expects full connection string, not UseSqliteVec which expects just the path.
                    // REQ-FN-048: a null-coalesce was never enough. VectorStoreConfig.ConnectionString is
                    // NON-NULLABLE and defaults to the relative literal "Data Source=techierag.db", so the
                    // fallback below could not fire and every saved config carried a CWD-relative path
                    // straight through to SQLite. Anything relative is resolved into the data directory here.
                    builder.UseVectorStore(VectorStoreType.SqliteVec,
                        DataDirectory.ResolveSqliteConnectionString(
                            savedConfig.VectorStore.ConnectionString,
                            ResolveDataDirectory(),
                            DataDirectory.VectorDbFileName));
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
                    builder.UseVectorStore(VectorStoreType.SqliteVec, DefaultVectorConnectionString());
                    break;
            }

            // Apply processing settings
            builder.WithChunkSize(
                savedConfig.Processing.DefaultChunkSize,
                savedConfig.Processing.DefaultChunkOverlap);

            // Configure LLM provider. REQ-FN-052: the SAME selection ResolveConfiguredLlmAsync
            // reports, so what a test observes is what the instance is actually built with.
            var primaryLlm = SelectBuildableLlm(savedConfig.Llm, "primary");
            if (primaryLlm is not null)
            {
                builder.UseLlm(
                    primaryLlm.Source,
                    primaryLlm.Endpoint,
                    primaryLlm.ApiKey,
                    primaryLlm.Model,
                    primaryLlm.Temperature,
                    primaryLlm.MaxTokens);
            }

            // Configure fallback LLM (REQ-UI-043: same unbuildable-config guard as the primary)
            var fallbackLlm = SelectBuildableLlm(savedConfig.LlmFallback, "fallback");
            if (fallbackLlm is not null)
            {
                builder.WithFallbackLlm(fb =>
                {
                    fb.Source = fallbackLlm.Source;
                    fb.Endpoint = fallbackLlm.Endpoint;
                    fb.ApiKey = fallbackLlm.ApiKey;
                    fb.Model = fallbackLlm.Model;
                    fb.Temperature = fallbackLlm.Temperature;
                    fb.MaxTokens = fallbackLlm.MaxTokens;
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

            // Wave 3 (REQ-RAG-026): honor the configured chunking strategy during ingestion.
            builder.WithChunking(savedConfig.Processing.ChunkingStrategy);

            // Wave 3 (REQ-RAG-014/REQ-RAG-025), revised by REQ-RAG-047: build the reranker whenever a
            // usable source is configured — NOT only when Rerank.Enabled is true. A workspace can force
            // rerank ON via Workspace.RerankEnabled while the instance default is off, and that is
            // impossible if no IReranker was ever constructed. Rerank.Enabled now selects only the
            // DEFAULT for calls that do not specify a per-call switch.
            var rerank = savedConfig.Rerank;
            if (rerank.Source is RerankSource.Cohere or RerankSource.Jina &&
                !string.IsNullOrEmpty(rerank.ApiKey))
            {
                builder.WithReranker(rerank.Source, rerank.ApiKey!, rerank.Model, rerank.Endpoint,
                    rerank.TopN, rerank.CandidateCount);
            }
            else if (rerank.Source is RerankSource.LocalOnnx)
            {
                // The bundled ONNX cross-encoder — the only reranker reachable with no API key,
                // and therefore the only way the per-workspace toggle is observable offline.
                builder.UseEmbeddedReranker(rerank.ModelPath, rerank.TopN, rerank.CandidateCount);
            }

            builder.WithRerankEnabledByDefault(rerank.Enabled);
        }
        else
        {
            // Use defaults
            builder.UseEmbedded();
            builder.UseVectorStore(VectorStoreType.SqliteVec, DefaultVectorConnectionString());
        }

        // Wave 3 shared re-wiring (REQ-RAG-007/008/028): enable relational persistence so the
        // library self-creates and owns its TrThread/TrMessage/TrWorkspace/TrWorkspaceDocument
        // tables. This backs workspaces (WorkspaceManager) and thread-aware chat history
        // (IConversationStore). The store keeps its OWN SQLite file — separate ownership from the
        // DbUp-owned app DB and the vector store, since the library is a reusable SDK whose schema
        // must survive being pointed at Postgres — but REQ-FN-034 puts all three in ONE directory.
        var dataDirectory = ResolveDataDirectory();
        var persistenceDbPath = Path.Combine(dataDirectory, DataDirectory.RagStoreFileName);
        builder.WithPersistence(StoreProvider.Sqlite, $"Data Source={persistenceDbPath}");

        return builder.Build();
    }

    #region ITechieRag Implementation (forwarding to current instance)

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        await instance.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> IngestAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return await instance.IngestAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> IngestTextAsync(string text, string documentName, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return await instance.IngestTextAsync(text, documentName, metadata, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> IngestDirectoryAsync(string directoryPath, string searchPattern = "*.*", CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return await instance.IngestDirectoryAsync(directoryPath, searchPattern, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return await instance.SearchAsync(query, topK, documentFilter, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches the indexed documents using per-call options, including the REQ-RAG-047 rerank switch.
    /// </summary>
    /// <remarks>
    /// This override is REQUIRED. <see cref="ITechieRag"/> ships a default interface implementation that
    /// forwards to the legacy overload and therefore silently DROPS <see cref="SearchOptions.Rerank"/>.
    /// Because this class is a facade over the real client, inheriting that default would make the
    /// per-call switch a no-op for every caller that goes through the manager.
    /// </remarks>
    /// <param name="query">The natural-language query to search for.</param>
    /// <param name="options">Per-call search options; null uses the configured defaults.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The matching search results, reranked when the options or configuration ask for it.</returns>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, SearchOptions? options, CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return await instance.SearchAsync(query, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        await instance.DeleteDocumentAsync(documentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return await instance.ListDocumentsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return await instance.GetStatsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        await instance.ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RagResponse> AskAsync(
        string question,
        int topK = 5,
        string? systemPrompt = null,
        string? documentFilter = null,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return await instance.AskAsync(question, topK, systemPrompt, documentFilter, options, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> AskStreamAsync(
        string question,
        int topK = 5,
        string? systemPrompt = null,
        string? documentFilter = null,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        await foreach (var token in instance.AskStreamAsync(question, topK, systemPrompt, documentFilter, options, cancellationToken).ConfigureAwait(false))
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
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return await instance.ChatWithRagAsync(userMessage, conversationHistory, topK, systemPrompt, options, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> ChatWithRagStreamAsync(
        string userMessage,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        int topK = 5,
        string? systemPrompt = null,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        await foreach (var token in instance.ChatWithRagStreamAsync(userMessage, conversationHistory, topK, systemPrompt, options, cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    /// <summary>
    /// Asynchronously resolves the configured LLM provider from the current instance, awaiting a
    /// cold-start build if needed. Use this instead of the sync <see cref="ITechieRag.GetLlmProvider"/>:
    /// on a cold instance the sync path blocked the Blazor circuit thread inside the instance lock and
    /// deadlocked the whole app (TR-RAG-005).
    /// </summary>
    public async Task<ILlmProvider?> GetLlmProviderAsync()
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return instance.GetLlmProvider();
    }

    /// <summary>
    /// Asynchronously resolves the token tracker from the current instance, awaiting a cold-start
    /// build if needed. Use this instead of the sync <see cref="ITechieRag.GetTokenTracker"/> to avoid
    /// the cold-start deadlock (TR-RAG-005).
    /// </summary>
    public async Task<ITokenTracker> GetTokenTrackerAsync()
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return instance.GetTokenTracker();
    }

    /// <summary>
    /// Asynchronously resolves the conversation memory from the current instance, awaiting a
    /// cold-start build if needed. Use this instead of the sync
    /// <see cref="ITechieRag.GetConversationMemory"/> to avoid the cold-start deadlock (TR-RAG-005).
    /// </summary>
    public async Task<IConversationMemory?> GetConversationMemoryAsync()
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return instance.GetConversationMemory();
    }

    /// <summary>
    /// Asynchronously resolves the workspace manager from the current instance, awaiting a
    /// cold-start build if needed (Wave 3, REQ-RAG-028). The default <see cref="ITechieRag"/>
    /// interface accessors return null, so this forwarding accessor is required for consumers
    /// holding a <see cref="TechieRagManager"/> reference to reach the built instance's manager.
    /// </summary>
    /// <returns>The library <see cref="WorkspaceManager"/>, or null when persistence is off.</returns>
    /// <remarks>
    /// Virtual so a test can substitute a manager built over a temporary store without standing up
    /// an embedding provider and a vector database (REQ-FN-041 workspace-listing coverage).
    /// </remarks>
    public virtual async Task<WorkspaceManager?> GetWorkspaceManagerAsync()
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return instance.GetWorkspaceManager();
    }

    /// <summary>
    /// Asynchronously resolves the persistent conversation store from the current instance,
    /// awaiting a cold-start build if needed (Wave 3, REQ-RAG-008/027).
    /// </summary>
    /// <returns>The library <see cref="IConversationStore"/>, or null when persistence is off.</returns>
    public async Task<IConversationStore?> GetConversationStoreAsync()
    {
        var instance = await GetInstanceAsync().ConfigureAwait(false);
        return instance.GetConversationStore();
    }

    /// <inheritdoc />
    WorkspaceManager? ITechieRag.GetWorkspaceManager()
        => currentInstance?.GetWorkspaceManager();

    /// <inheritdoc />
    IConversationStore? ITechieRag.GetConversationStore()
        => currentInstance?.GetConversationStore();

    // The ITechieRag contract (core library, cannot be changed here) mandates these SYNCHRONOUS
    // accessors. They are implemented EXPLICITLY so consumers holding a TechieRagManager reference are
    // steered to the async accessors above; and they NEVER sync-over-async (the TR-RAG-005 deadlock) —
    // they read the already-built instance only, degrading gracefully when the instance is still cold.

    /// <inheritdoc />
    ILlmProvider? ITechieRag.GetLlmProvider()
        => currentInstance?.GetLlmProvider();

    /// <inheritdoc />
    ITokenTracker ITechieRag.GetTokenTracker()
        => currentInstance?.GetTokenTracker()
           ?? throw new InvalidOperationException(
               "TechieRag instance is not initialized yet. Await a TechieRag operation " +
               "(or GetTokenTrackerAsync) before requesting the token tracker synchronously.");

    /// <inheritdoc />
    IConversationMemory? ITechieRag.GetConversationMemory()
        => currentInstance?.GetConversationMemory();

    #endregion

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        if (currentInstance is IDisposable disposable)
        {
            disposable.Dispose();
        }

        instanceLock.Dispose();
    }
}
