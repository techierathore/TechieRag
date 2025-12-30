using System.Text.Json;
using TechieRag;

namespace TechieRagWeb.Services;

/// <summary>
/// Service for managing TechieRag runtime configuration.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides methods to load and save TechieRag configuration
/// at runtime, enabling dynamic configuration changes from the Settings page.</para>
/// <para><b>Code Flow:</b> Registered as a scoped service in Program.cs. Injected into
/// Settings.razor to load current configuration and save user changes.</para>
/// <para><b>Storage:</b> Configuration is loaded from appsettings.json and can be
/// saved to a separate JSON file for demonstration purposes.</para>
/// </remarks>
public class TechieRagConfigService
{
    private readonly IConfiguration configuration;
    private readonly IWebHostEnvironment environment;
    private readonly ILogger<TechieRagConfigService> logger;
    private TechieRagConfig? cachedConfig;
    private readonly string configFilePath;

    /// <summary>
    /// Creates a new TechieRagConfigService instance.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The web host environment for file paths.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public TechieRagConfigService(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<TechieRagConfigService> logger)
    {
        this.configuration = configuration;
        this.environment = environment;
        this.logger = logger;
        this.configFilePath = Path.Combine(environment.ContentRootPath, "techierag-config.json");
    }

    /// <summary>
    /// Loads the current TechieRag configuration.
    /// </summary>
    /// <returns>The current TechieRag configuration.</returns>
    /// <remarks>
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item>First checks for saved config file (techierag-config.json)</item>
    /// <item>If not found, falls back to appsettings.json configuration</item>
    /// <item>If neither exists, returns default configuration</item>
    /// </list>
    /// </remarks>
    public async Task<TechieRagConfig> LoadConfigAsync()
    {
        if (cachedConfig != null)
        {
            return cachedConfig;
        }

        try
        {
            // First try to load from saved config file
            if (File.Exists(configFilePath))
            {
                var json = await File.ReadAllTextAsync(configFilePath);
                var savedConfig = JsonSerializer.Deserialize<TechieRagConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (savedConfig != null)
                {
                    cachedConfig = savedConfig;
                    logger.LogInformation("Loaded configuration from {Path}", configFilePath);
                    return cachedConfig;
                }
            }

            // Fall back to appsettings.json
            var section = configuration.GetSection("TechieRag");
            if (section.Exists())
            {
                cachedConfig = new TechieRagConfig
                {
                    Embedding = new EmbeddingConfig
                    {
                        Source = Enum.TryParse<EmbeddingSource>(section["Embedding:Source"], out var source)
                            ? source
                            : EmbeddingSource.Ollama,
                        Endpoint = section["Embedding:Endpoint"] ?? "http://localhost:11434",
                        Model = section["Embedding:Model"] ?? "bge-m3",
                        ApiKey = section["Embedding:ApiKey"],
                        ModelPath = section["Embedding:ModelPath"]
                    },
                    VectorStore = new VectorStoreConfig
                    {
                        Type = Enum.TryParse<VectorStoreType>(section["VectorStore:Type"], out var storeType)
                            ? storeType
                            : VectorStoreType.SqliteVec,
                        ConnectionString = section["VectorStore:ConnectionString"] ?? "Data Source=techierag.db",
                        ApiKey = section["VectorStore:ApiKey"]
                    },
                    Processing = new ProcessingConfig
                    {
                        DefaultChunkSize = int.TryParse(section["Processing:DefaultChunkSize"], out var chunkSize)
                            ? chunkSize
                            : 500,
                        DefaultChunkOverlap = int.TryParse(section["Processing:DefaultChunkOverlap"], out var overlap)
                            ? overlap
                            : 50
                    },
                    EnableTelemetry = bool.TryParse(section["EnableTelemetry"], out var telemetry)
                        ? telemetry
                        : true
                };

                logger.LogInformation("Loaded configuration from appsettings.json");
                return cachedConfig;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load configuration, using defaults");
        }

        // Return default configuration
        cachedConfig = new TechieRagConfig();
        logger.LogInformation("Using default configuration");
        return cachedConfig;
    }

    /// <summary>
    /// Saves the TechieRag configuration to a file.
    /// </summary>
    /// <param name="config">The configuration to save.</param>
    /// <remarks>
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item>Serializes the configuration to JSON</item>
    /// <item>Writes to techierag-config.json in the application root</item>
    /// <item>Updates the cached configuration</item>
    /// </list>
    /// <para><b>Note:</b> This saves to a separate file rather than modifying appsettings.json
    /// to avoid issues with configuration reloading and to keep user changes separate.</para>
    /// </remarks>
    /// <exception cref="IOException">Thrown when the file cannot be written.</exception>
    public async Task SaveConfigAsync(TechieRagConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(configFilePath, json);
            cachedConfig = config;

            logger.LogInformation("Saved configuration to {Path}", configFilePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save configuration to {Path}", configFilePath);
            throw;
        }
    }

    /// <summary>
    /// Clears the cached configuration, forcing a reload on next access.
    /// </summary>
    /// <remarks>
    /// Call this method when you want to reload configuration from disk.
    /// </remarks>
    public void ClearCache()
    {
        cachedConfig = null;
        logger.LogDebug("Configuration cache cleared");
    }

    /// <summary>
    /// Deletes the saved configuration file and clears the cache.
    /// </summary>
    /// <remarks>
    /// This will cause the next LoadConfigAsync call to fall back to appsettings.json.
    /// </remarks>
    public void ResetToDefaults()
    {
        if (File.Exists(configFilePath))
        {
            File.Delete(configFilePath);
            logger.LogInformation("Deleted saved configuration file");
        }

        cachedConfig = null;
    }
}
