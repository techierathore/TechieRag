using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TechieRag.DependencyInjection;

/// <summary>
/// Extension methods for registering TechieRag services with dependency injection.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides convenient methods to add TechieRag to an ASP.NET Core
/// or generic .NET host application's service collection.</para>
/// <para><b>Code Flow:</b> Called in Program.cs or Startup.ConfigureServices to register
/// ITechieRag and TechieRagConfig as singletons. The actual TechieRag instance is created
/// lazily when first requested from the DI container.</para>
/// <para><b>Usage:</b> Call AddTechieRag in Program.cs to register ITechieRag.</para>
/// <para><b>Example:</b></para>
/// <code>
/// // Using fluent builder
/// services.AddTechieRag(builder => builder
///     .UseOllama()
///     .UseSqliteVec());
///
/// // Using configuration
/// services.AddTechieRag(configuration.GetSection("TechieRag"));
/// </code>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds TechieRag services using a fluent builder configuration.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Action to configure the TechieRag builder.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item>Creates a new TechieRagBuilder instance</item>
    /// <item>Invokes the configure action to set up the builder</item>
    /// <item>Registers TechieRagConfig as a singleton</item>
    /// <item>Registers ITechieRag as a singleton with deferred creation</item>
    /// </list>
    /// <para><b>Note:</b> The ITechieRag instance is created lazily. When resolved,
    /// it automatically injects ILoggerFactory from the DI container.</para>
    /// <para><b>Example:</b></para>
    /// <code>
    /// services.AddTechieRag(builder => builder
    ///     .UseOllama("http://localhost:11434", "bge-m3")
    ///     .UseSqliteVec("myapp.db")
    ///     .WithChunkSize(500, 50));
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when services or configure is null.
    /// </exception>
    public static IServiceCollection AddTechieRag(
        this IServiceCollection services,
        Action<TechieRagBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new TechieRagBuilder();
        configure(builder);

        // Register the configuration as a singleton
        services.AddSingleton(builder.GetConfig());

        // Register ITechieRag with deferred creation
        // This allows the ILoggerFactory to be resolved from DI
        services.AddSingleton<ITechieRag>(sp =>
        {
            // Inject logger factory from DI container
            var loggerFactory = sp.GetService<ILoggerFactory>();
            if (loggerFactory != null)
            {
                builder.WithLogging(loggerFactory);
            }

            return builder.Build();
        });

        return services;
    }

    /// <summary>
    /// Adds TechieRag services using configuration from IConfiguration (e.g., appsettings.json).
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Configuration section containing TechieRag settings.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item>Binds the configuration section to TechieRagConfig</item>
    /// <item>Validates the configuration was found</item>
    /// <item>Delegates to the builder-based AddTechieRag overload</item>
    /// </list>
    /// <para><b>Expected Configuration Structure:</b></para>
    /// <code>
    /// {
    ///   "TechieRag": {
    ///     "Embedding": {
    ///       "Source": "Ollama",
    ///       "Endpoint": "http://localhost:11434",
    ///       "Model": "bge-m3"
    ///     },
    ///     "VectorStore": {
    ///       "Type": "SqliteVec",
    ///       "ConnectionString": "Data Source=techierag.db"
    ///     },
    ///     "Processing": {
    ///       "DefaultChunkSize": 500,
    ///       "DefaultChunkOverlap": 50
    ///     },
    ///     "EnableTelemetry": true
    ///   }
    /// }
    /// </code>
    /// <para><b>Usage:</b></para>
    /// <code>
    /// // In Program.cs
    /// builder.Services.AddTechieRag(builder.Configuration.GetSection("TechieRag"));
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when services or configuration is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the configuration section is missing or cannot be bound.
    /// </exception>
    public static IServiceCollection AddTechieRag(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind configuration to TechieRagConfig
        var config = configuration.Get<TechieRagConfig>()
            ?? throw new InvalidOperationException(
                "TechieRag configuration section not found or could not be bound. " +
                "Ensure the configuration section exists and contains valid TechieRag settings.");

        // Use the builder-based registration with configuration values
        return services.AddTechieRag(builder =>
        {
            // Configure embedding
            builder.UseEmbedding(
                config.Embedding.Source,
                config.Embedding.Endpoint,
                config.Embedding.ApiKey,
                config.Embedding.Model,
                config.Embedding.ModelPath);

            // Configure vector store
            builder.UseVectorStore(
                config.VectorStore.Type,
                config.VectorStore.ConnectionString);

            // Configure processing
            builder.WithChunkSize(
                config.Processing.DefaultChunkSize,
                config.Processing.DefaultChunkOverlap);
            builder.WithChunking(config.Processing.ChunkingStrategy);

            // Configure telemetry
            builder.WithTelemetry(config.EnableTelemetry);

            // Configure reranking (if specified)
            if (config.Rerank.Enabled && config.Rerank.Source is RerankSource.Cohere or RerankSource.Jina)
            {
                builder.WithReranker(
                    config.Rerank.Source,
                    config.Rerank.ApiKey ?? string.Empty,
                    config.Rerank.Model,
                    config.Rerank.Endpoint,
                    config.Rerank.TopN,
                    config.Rerank.CandidateCount);
            }

            // Configure persistence (if specified)
            if (config.Persistence.Provider != StoreProvider.None && config.Persistence.ConnectionString is not null)
            {
                builder.WithPersistence(
                    config.Persistence.Provider,
                    config.Persistence.ConnectionString,
                    config.Persistence.DefaultUserId);
            }

            // Configure LLM (if specified)
            if (config.Llm.Source != LlmSource.None)
            {
                builder.UseLlm(
                    config.Llm.Source,
                    config.Llm.Endpoint,
                    config.Llm.ApiKey,
                    config.Llm.Model,
                    config.Llm.Temperature,
                    config.Llm.MaxTokens);
            }

            // Configure fallback LLM
            if (config.LlmFallback is not null && config.LlmFallback.Source != LlmSource.None)
            {
                builder.WithFallbackLlm(fb =>
                {
                    fb.Source = config.LlmFallback.Source;
                    fb.Endpoint = config.LlmFallback.Endpoint;
                    fb.ApiKey = config.LlmFallback.ApiKey;
                    fb.Model = config.LlmFallback.Model;
                    fb.Temperature = config.LlmFallback.Temperature;
                    fb.MaxTokens = config.LlmFallback.MaxTokens;
                });
            }

            // Configure usage tracking
            if (config.UsageTracking.Enabled)
            {
                builder.WithUsageTracking(tracking =>
                {
                    tracking.MaxTotalTokens = config.UsageTracking.MaxTotalTokens;
                    tracking.MaxCostUsd = config.UsageTracking.MaxCostUsd;
                    tracking.AlertThreshold = config.UsageTracking.AlertThreshold;
                    tracking.BlockOnExceeded = config.UsageTracking.BlockOnExceeded;
                    tracking.Pricing = config.UsageTracking.Pricing;
                });
            }

            // Configure resilience
            builder.WithResilience(r =>
            {
                r.MaxRetries = config.Resilience.MaxRetries;
                r.InitialRetryDelayMs = config.Resilience.InitialRetryDelayMs;
                r.MaxRetryDelayMs = config.Resilience.MaxRetryDelayMs;
                r.BackoffMultiplier = config.Resilience.BackoffMultiplier;
                r.HandleRateLimiting = config.Resilience.HandleRateLimiting;
                r.CircuitBreakerThreshold = config.Resilience.CircuitBreakerThreshold;
                r.CircuitBreakerRecoverySeconds = config.Resilience.CircuitBreakerRecoverySeconds;
                r.TimeoutSeconds = config.Resilience.TimeoutSeconds;
            });
        });
    }

    /// <summary>
    /// Adds TechieRag services using a pre-configured TechieRagConfig object.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="config">The pre-configured TechieRagConfig instance.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para><b>Purpose:</b> Allows registration with an already-configured config object.
    /// Useful when configuration comes from a custom source or is built programmatically.</para>
    /// <para><b>Example:</b></para>
    /// <code>
    /// var config = new TechieRagConfig
    /// {
    ///     Embedding = new EmbeddingConfig { Source = EmbeddingSource.Ollama },
    ///     VectorStore = new VectorStoreConfig { Type = VectorStoreType.SqliteVec }
    /// };
    /// services.AddTechieRag(config);
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when services or config is null.
    /// </exception>
    public static IServiceCollection AddTechieRag(
        this IServiceCollection services,
        TechieRagConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        return services.AddTechieRag(builder =>
        {
            builder.UseEmbedding(
                config.Embedding.Source,
                config.Embedding.Endpoint,
                config.Embedding.ApiKey,
                config.Embedding.Model,
                config.Embedding.ModelPath);

            builder.UseVectorStore(
                config.VectorStore.Type,
                config.VectorStore.ConnectionString);

            builder.WithChunkSize(
                config.Processing.DefaultChunkSize,
                config.Processing.DefaultChunkOverlap);

            builder.WithTelemetry(config.EnableTelemetry);
        });
    }
}
