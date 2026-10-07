using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // REQ-RAG-116: services built on workspaces take IWorkspaceManager. TryAdd keeps a stand-in
        // the host registered first. Without persistence there is no workspace manager, and the
        // resolve fails with a message that says so rather than handing out null.
        services.TryAddSingleton<Abstractions.IWorkspaceManager>(sp =>
            sp.GetRequiredService<ITechieRag>().GetWorkspaceManager()
            ?? throw new InvalidOperationException(
                "IWorkspaceManager is not available: workspaces need persistence. Call WithPersistence(...) "
                + "on the TechieRag builder, or register your own IWorkspaceManager before AddTechieRag."));

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
    /// Thrown when the configuration section is missing or cannot be bound, or when it holds a key
    /// (any <c>ApiKey</c> or <c>Headers</c> value): keys are passed only in code.
    /// </exception>
    public static IServiceCollection AddTechieRag(
        this IServiceCollection services,
        IConfiguration configuration)
        => AddFromConfiguration(services, configuration, configure: null);

    /// <summary>
    /// Adds TechieRag services from an <see cref="IConfiguration"/> section, then applies the host's code,
    /// which is where keys and dependencies are supplied (REQ-FN-066 / BRD-159).
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The <c>TechieRag</c> section; it must hold no keys.</param>
    /// <param name="configure">Runs after the section is mapped: pass keys with
    /// <see cref="TechieRagBuilder.WithApiKeys"/> and <see cref="TechieRagBuilder.WithLlmHeaders"/>, or any
    /// other builder call such as a custom provider.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <code>
    /// builder.Services.AddTechieRag(
    ///     builder.Configuration.GetSection("TechieRag"),
    ///     rag => rag.WithApiKeys(llm: secrets.OpenAiKey, vectorStore: secrets.QdrantKey));
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when services, configuration or configure is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the section is missing or cannot be bound,
    /// or when it holds a key; the message names each key's setting, never its value.</exception>
    public static IServiceCollection AddTechieRag(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TechieRagBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return AddFromConfiguration(services, configuration, configure);
    }

    private static IServiceCollection AddFromConfiguration(
        IServiceCollection services,
        IConfiguration configuration,
        Action<TechieRagBuilder>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind configuration to TechieRagConfig
        var config = configuration.Get<TechieRagConfig>()
            ?? throw new InvalidOperationException(
                "TechieRag configuration section not found or could not be bound. " +
                "Ensure the configuration section exists and contains valid TechieRag settings.");

        // Keys and dependencies are injected only through code (owner decision 2026-10-01).
        ConfigurationKeyGuard.ThrowIfKeysPresent(config);

        // REQ-FN-066: one mapper for every overload, so every bound field reaches the builder.
        return services.AddTechieRag(builder => TechieRagConfigMapper.Apply(builder, config, configure));
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

        return services.AddTechieRag(builder => TechieRagConfigMapper.Apply(builder, config));
    }
}
