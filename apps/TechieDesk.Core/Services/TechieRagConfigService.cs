using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using TechieRag;

using TechieDesk.Services.Hosting;
using TechieDeskDb;

namespace TechieDesk.Services;

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
/// <para><b>Security (REQ-NFR-004, REQ-NFR-012):</b> provider API keys and the vector-store and
/// persistence connection strings are encrypted at rest by <see cref="TechieRagConfigProtector"/>
/// before the file is written and decrypted after it is read, so <c>techierag-config.json</c> never
/// contains cleartext credentials — including the <c>Password=…</c> embedded in a PgVector or
/// Postgres DSN. Callers always see cleartext values in memory — the app must replay them to the
/// providers and data stores.</para>
/// </remarks>
public class TechieRagConfigService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Options used for the in-memory deep copy handed to callers (REQ-FN-052).</summary>
    private static readonly JsonSerializerOptions CopyOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfiguration configuration;
    private readonly IAppEnvironment environment;
    private readonly ILogger<TechieRagConfigService> logger;
    private readonly TechieRagConfigProtector configProtector;
    private TechieRagConfig? cachedConfig;
    private readonly string configFilePath;
    private readonly string dataDirectory;

    /// <summary>
    /// Creates a new TechieRagConfigService instance.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The application environment, used to resolve legacy file paths.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="dataProtectionProvider">Data Protection provider used to encrypt provider API
    /// keys at rest (REQ-NFR-004).</param>
    /// <param name="loggerFactory">Logger factory used to log the protector's security events.</param>
    /// <param name="secretStore">The OS credential store provider API keys are kept in (REQ-FN-039).
    /// Optional so a host without a platform store still falls back to encryption at rest; the DI
    /// container supplies it whenever one is registered.</param>
    public TechieRagConfigService(
        IConfiguration configuration,
        IAppEnvironment environment,
        ILogger<TechieRagConfigService> logger,
        IDataProtectionProvider dataProtectionProvider,
        ILoggerFactory loggerFactory,
        TechieDesk.Services.Auth.ISecretStore? secretStore = null)
    {
        this.configuration = configuration;
        this.environment = environment;
        this.logger = logger;
        this.configProtector = new TechieRagConfigProtector(
            dataProtectionProvider,
            loggerFactory.CreateLogger<TechieRagConfigProtector>(),
            secretStore);
        // REQ-FN-034/REQ-FN-037: the saved provider configuration lives in the ONE data directory,
        // which since REQ-FN-037 is the per-user OS location. It previously sat in the content root —
        // for the desktop head, inside the read-only .app bundle — so every saved provider setting
        // (and, since REQ-NFR-004b, every encrypted API key) was unwritable on a signed install.
        this.dataDirectory = DataDirectory.ResolveAndCreate(configuration[DataDirectory.ConfigKey]);
        // REQ-FN-052: derived from the one authority, never spelled here. TechieRagManager derives the
        // same path from the same helper, so the screen's write and the RAG instance's read cannot
        // drift onto two files.
        this.configFilePath = DataDirectory.ConfigFilePath(this.dataDirectory);

        var legacyConfigPath = Path.Combine(environment.ContentRootPath, DataDirectory.ConfigFileName);
        if (DataDirectory.RelocateLegacyArtefact(legacyConfigPath, this.configFilePath))
        {
            logger.LogInformation(
                "Relocated the saved provider configuration into the data directory (REQ-FN-034/037): {From} -> {To}",
                legacyConfigPath, this.configFilePath);
        }
    }

    /// <summary>
    /// Gets the absolute path of the ONE file this service reads and writes (REQ-FN-052).
    /// </summary>
    /// <remarks>
    /// Exposed so a test can assert that this writer and <see cref="TechieRagManager"/>'s reader name
    /// the same file — the acceptance clause of REQ-FN-052 that a value comparison cannot satisfy,
    /// because two independent stores holding equal values look identical until they disagree.
    /// </remarks>
    public string ConfigFilePath => configFilePath;

    /// <summary>
    /// Produces an independent deep copy of a configuration (REQ-FN-052).
    /// </summary>
    /// <param name="config">The configuration to copy.</param>
    /// <returns>A copy sharing no mutable object with <paramref name="config"/>.</returns>
    /// <remarks>
    /// <para><b>This is the fix for the second store.</b> <see cref="LoadConfigAsync"/> used to hand
    /// every screen the SAME <see cref="cachedConfig"/> instance, and every settings screen binds its
    /// form fields straight onto it. So a provider the operator merely SELECTED — never successfully
    /// saved — was written into the service's cache by the two-way binding, and every later read
    /// (the same screen re-entered, the layout, App Settings) reported it back as if it were the saved
    /// configuration. The RAG instance builds from the FILE and therefore disagreed: the "page and the
    /// RAG instance read different stores" symptom, with the page's store written by a keystroke.</para>
    /// <para>With a copy the in-memory projection is <b>derived</b> from the file and is replaced only
    /// by a disk read or by a write that succeeded — which is acceptance clause (4).</para>
    /// </remarks>
    private static TechieRagConfig CopyConfig(TechieRagConfig config) =>
        JsonSerializer.Deserialize<TechieRagConfig>(
            JsonSerializer.Serialize(config, CopyOptions), CopyOptions) ?? new TechieRagConfig();

    /// <summary>
    /// Rewrites a relative SqliteVec store path into the data directory and moves any artefact the
    /// relative path already produced (REQ-FN-048).
    /// </summary>
    /// <param name="config">The configuration to correct in place.</param>
    /// <returns>True when the connection string was changed and the file needs rewriting.</returns>
    /// <remarks>
    /// <para>
    /// This is acceptance clause (2). Every install that has ever saved provider settings carries
    /// <c>"connectionString": "Data Source=techierag.db"</c> — the non-nullable default of
    /// <c>VectorStoreConfig</c> — which SQLite resolves against the process working directory. On the
    /// desktop head that is the <c>.app</c> bundle root, so the vector database was created INSIDE a
    /// signed bundle and <c>codesign --verify</c> failed on it.
    /// </para>
    /// <para>
    /// The already-created file is MOVED rather than left behind: it holds the install's embeddings,
    /// and orphaning it would silently reset a populated document library to empty. Only the SqliteVec
    /// store is touched — a PgVector or Qdrant connection string is not a path.
    /// </para>
    /// </remarks>
    private bool ResolveVectorStorePath(TechieRagConfig config)
    {
        if (config.VectorStore.Type != VectorStoreType.SqliteVec)
        {
            return false;
        }

        var configured = config.VectorStore.ConnectionString;
        var resolved = DataDirectory.ResolveSqliteConnectionString(
            configured, dataDirectory, DataDirectory.VectorDbFileName);
        if (string.Equals(configured, resolved, StringComparison.Ordinal))
        {
            return false;
        }

        config.VectorStore.ConnectionString = resolved;

        var strayArtefact = Path.GetFullPath(DataDirectory.VectorDbFileName);
        var currentArtefact = Path.Combine(dataDirectory, DataDirectory.VectorDbFileName);
        if (DataDirectory.RelocateLegacyArtefact(strayArtefact, currentArtefact))
        {
            logger.LogInformation(
                "Relocated the vector database out of the working directory (REQ-FN-048): {From} -> {To}",
                strayArtefact, currentArtefact);
        }

        logger.LogInformation(
            "Resolved the relative vector-store connection string against the data directory "
            + "(REQ-FN-048): {Configured} -> {Resolved}",
            configured, resolved);
        return true;
    }

    /// <summary>
    /// Loads the current TechieRag configuration.
    /// </summary>
    /// <returns>The current TechieRag configuration, with any encrypted API key decrypted.</returns>
    /// <remarks>
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item>First checks for saved config file (techierag-config.json)</item>
    /// <item>Decrypts provider API keys, transparently upgrading legacy cleartext values</item>
    /// <item>If not found, falls back to appsettings.json configuration</item>
    /// <item>If neither exists, returns default configuration</item>
    /// </list>
    /// <para><b>REQ-FN-052:</b> the caller always receives its OWN copy, never the cached instance.
    /// Screens bind their form fields directly to what this returns, so handing out the cache made
    /// every keystroke on a settings screen the application's apparent configuration — including a
    /// provider whose save was then refused. See <see cref="CopyConfig"/>.</para>
    /// </remarks>
    public async Task<TechieRagConfig> LoadConfigAsync()
    {
        if (cachedConfig != null)
        {
            return CopyConfig(cachedConfig);
        }

        try
        {
            var savedConfig = await TryLoadSavedConfigAsync();
            if (savedConfig != null)
            {
                cachedConfig = savedConfig;
                return CopyConfig(cachedConfig);
            }

            var fromAppSettings = BuildConfigFromAppSettings();
            if (fromAppSettings != null)
            {
                cachedConfig = fromAppSettings;
                logger.LogInformation("Loaded configuration from appsettings.json");
                return CopyConfig(cachedConfig);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load configuration, using defaults");
        }

        // REQ-FN-048: TechieRagConfig's own SqliteVec default is the relative "Data Source=techierag.db".
        // The library is free to keep it — a console consumer's working directory IS its data
        // directory — but this application has one, so it resolves it before anyone opens the store.
        var defaults = new TechieRagConfig();
        ResolveVectorStorePath(defaults);
        cachedConfig = defaults;
        logger.LogInformation("Using default configuration");
        return CopyConfig(cachedConfig);
    }

    /// <summary>
    /// Reads the saved configuration file and decrypts its provider API keys.
    /// </summary>
    /// <returns>The saved configuration, or null when no usable file exists.</returns>
    /// <remarks>
    /// When the file still holds legacy cleartext credentials it is transparently rewritten in the
    /// encrypted form. A value that cannot be decrypted is dropped from the returned configuration
    /// and the file is deliberately left untouched.
    /// </remarks>
    private async Task<TechieRagConfig?> TryLoadSavedConfigAsync()
    {
        if (!File.Exists(configFilePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(configFilePath);
        var savedConfig = JsonSerializer.Deserialize<TechieRagConfig>(json, ReadOptions);
        if (savedConfig == null)
        {
            return null;
        }

        var needsEncryptionUpgrade = configProtector.RevealSecrets(savedConfig);
        logger.LogInformation("Loaded configuration from {Path}", configFilePath);

        // REQ-FN-048: migrate a relative vector-store path on FIRST LOAD, before anything opens it.
        // WriteConfigAsync applies the same correction, so a save alone would fix the file — but only
        // once the user next visits a settings screen, and the store is opened long before that.
        var needsPathMigration = ResolveVectorStorePath(savedConfig);

        if (needsEncryptionUpgrade || needsPathMigration)
        {
            await WriteConfigAsync(savedConfig);
            logger.LogInformation(
                "Rewrote {ConfigPath} (credential upgrade: {CredentialUpgrade}, "
                + "vector-store path migration: {PathMigration})",
                configFilePath, needsEncryptionUpgrade, needsPathMigration);
        }

        return savedConfig;
    }

    /// <summary>
    /// Builds a configuration from the <c>TechieRag</c> section of appsettings.json / environment.
    /// </summary>
    /// <returns>The bound configuration, or null when the section is absent.</returns>
    private TechieRagConfig? BuildConfigFromAppSettings()
    {
        var section = configuration.GetSection("TechieRag");
        if (!section.Exists())
        {
            return null;
        }

        var fromAppSettings = new TechieRagConfig
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
                // REQ-FN-048: left as configured here and resolved by ResolveVectorStorePath below,
                // which knows the store type — an absent value must NOT become the relative
                // "Data Source=techierag.db" that resolves against the process working directory, and
                // a Qdrant endpoint URL must not be mistaken for a file path.
                ConnectionString = section["VectorStore:ConnectionString"] ?? string.Empty,
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
            EnableTelemetry = !bool.TryParse(section["EnableTelemetry"], out var telemetry) || telemetry
        };

        ResolveVectorStorePath(fromAppSettings);
        return fromAppSettings;
    }

    /// <summary>
    /// Saves the TechieRag configuration to a file.
    /// </summary>
    /// <param name="config">The configuration to save.</param>
    /// <remarks>
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item>Encrypts every provider API key into a throwaway clone (REQ-NFR-004)</item>
    /// <item>Serializes that clone to JSON</item>
    /// <item>Writes to techierag-config.json in the application root</item>
    /// <item>Updates the cached configuration with the caller's cleartext instance</item>
    /// </list>
    /// <para><b>Note:</b> This saves to a separate file rather than modifying appsettings.json
    /// to avoid issues with configuration reloading and to keep user changes separate. No credential
    /// in the instance passed in is ever mutated, so the caller's bound UI keeps showing the
    /// cleartext key. The one field that IS corrected in place is a relative SqliteVec connection
    /// string (REQ-FN-048), deliberately: the caller's instance, the cache and the file must all name
    /// the same absolute database, or the screen would still be showing a path nothing opens.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    /// <exception cref="LlmConfigValidationException">Thrown when the LLM provider configuration
    /// cannot produce a working TechieRag instance (REQ-UI-043).</exception>
    /// <exception cref="IOException">Thrown when the file cannot be written.</exception>
    public Task SaveConfigAsync(TechieRagConfig config) =>
        SaveConfigAsync(config, TechieRagConfigSections.All);

    /// <summary>
    /// Saves only the sections the caller owns, leaving every other section as it is on disk
    /// (REQ-NFR-004).
    /// </summary>
    /// <param name="config">The caller's configuration. Never mutated.</param>
    /// <param name="sections">The sections this caller owns and intends to write.</param>
    /// <returns>A task that completes when the merged configuration has been written.</returns>
    /// <remarks>
    /// <para><b>This is the fix for a silent lost update.</b> Four screens write this one file and
    /// two pairs of them overlap (<c>Embedding</c>/<c>VectorStore</c> between App Settings and RAG
    /// Configuration; <c>Llm</c> between App Settings and LLM Settings). Each used to save the whole
    /// document from a copy loaded when its screen opened, so the second save silently reverted the
    /// first — success toast, opposite result. Re-reading here and copying across only the owned
    /// sections means a screen can no longer revert a field it does not edit.</para>
    /// <para>The re-read is deliberately from <b>disk</b>, not from <see cref="cachedConfig"/>: the
    /// cache is what the last writer put there, which is exactly the stale value being guarded
    /// against.</para>
    /// <para>This narrows a lost update, it is not a transaction. Two screens editing the SAME
    /// section still resolve last-writer-wins — which is honest, because they are genuinely editing
    /// the same field, and is what a single-user desktop app can reasonably promise.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when no section is named — a caller that
    /// owns nothing is a programming error, not a no-op worth performing.</exception>
    public async Task SaveConfigAsync(TechieRagConfig config, TechieRagConfigSections sections)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (sections == TechieRagConfigSections.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sections), "A save must name at least one section it owns.");
        }

        if (sections != TechieRagConfigSections.All)
        {
            config = await MergeOntoCurrentAsync(config, sections).ConfigureAwait(false);
        }

        // REQ-UI-043 / BRD-136: a half-configured provider must never reach disk. Saving an
        // OpenAI-compatible provider with a key but no endpoint used to succeed and then throw
        // "Endpoint is required for OpenAI-compatible LLM provider" on every unrelated page that
        // builds a TechieRag instance — /token-usage included. Refuse it here instead.
        var blockingErrors = LlmConfigValidator.ValidateConfig(config, buildBlockingOnly: true);
        if (blockingErrors.Count > 0)
        {
            logger.LogWarning(
                "Refused to save an unbuildable LLM configuration (REQ-UI-043): {Errors}",
                string.Join(" ", blockingErrors.Select(e => e.Describe())));
            throw new LlmConfigValidationException(blockingErrors);
        }

        try
        {
            await WriteConfigAsync(config);
            // REQ-FN-052: the cache is refreshed ONLY here, from a document that reached disk, and as
            // a copy — so the caller cannot keep editing the cache through the reference it just
            // handed in, and a save that threw above leaves the cache exactly as the file is.
            cachedConfig = CopyConfig(config);

            logger.LogInformation("Saved configuration to {Path}", configFilePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save configuration to {Path}", configFilePath);
            throw;
        }
    }

    /// <summary>
    /// Serializes an encrypted copy of the configuration to the saved-config file.
    /// </summary>
    /// <summary>Copies the caller's owned sections onto whatever is currently on disk.</summary>
    /// <param name="incoming">The caller's configuration. Never mutated.</param>
    /// <param name="sections">The sections the caller owns.</param>
    /// <returns>The merged configuration to write.</returns>
    /// <remarks>
    /// Reads through <see cref="TryLoadSavedConfigAsync"/> rather than <see cref="LoadConfigAsync"/>
    /// on purpose: the latter returns <see cref="cachedConfig"/>, which is whatever the last writer
    /// left there — the stale value this merge exists to defeat. The disk read reveals secrets and
    /// the write re-protects them, so cleartext-in / cleartext-out holds and nothing is
    /// double-encrypted.
    /// </remarks>
    private async Task<TechieRagConfig> MergeOntoCurrentAsync(
        TechieRagConfig incoming, TechieRagConfigSections sections)
    {
        var current = await TryLoadSavedConfigAsync().ConfigureAwait(false);
        if (current is null)
        {
            // Nothing saved yet, so there is no other writer's work to preserve.
            return incoming;
        }

        if (sections.HasFlag(TechieRagConfigSections.Embedding))
        {
            current.Embedding = incoming.Embedding;
        }

        if (sections.HasFlag(TechieRagConfigSections.VectorStore))
        {
            current.VectorStore = incoming.VectorStore;
        }

        if (sections.HasFlag(TechieRagConfigSections.Processing))
        {
            current.Processing = incoming.Processing;
        }

        if (sections.HasFlag(TechieRagConfigSections.Telemetry))
        {
            current.EnableTelemetry = incoming.EnableTelemetry;
        }

        if (sections.HasFlag(TechieRagConfigSections.Llm))
        {
            current.Llm = incoming.Llm;
        }

        if (sections.HasFlag(TechieRagConfigSections.LlmFallback))
        {
            current.LlmFallback = incoming.LlmFallback;
        }

        if (sections.HasFlag(TechieRagConfigSections.Prompt))
        {
            current.Prompt = incoming.Prompt;
        }

        if (sections.HasFlag(TechieRagConfigSections.Resilience))
        {
            current.Resilience = incoming.Resilience;
        }

        if (sections.HasFlag(TechieRagConfigSections.UsageTracking))
        {
            current.UsageTracking = incoming.UsageTracking;
        }

        if (sections.HasFlag(TechieRagConfigSections.Rerank))
        {
            current.Rerank = incoming.Rerank;
        }

        if (sections.HasFlag(TechieRagConfigSections.Persistence))
        {
            current.Persistence = incoming.Persistence;
        }

        logger.LogDebug("Merged {Sections} onto the configuration currently on disk", sections);
        return current;
    }

    private async Task WriteConfigAsync(TechieRagConfig config)
    {
        // REQ-FN-048: nothing relative reaches disk. Every screen that writes this file builds its
        // SqliteVec connection string from a file NAME the user typed ("techierag.db"), and the Setup
        // wizard writes an app-relative "data/…" path outright; each of those resolves against the
        // process working directory once something opens it. Correcting it here means no writer can
        // reintroduce the defect, whatever it hands in.
        ResolveVectorStorePath(config);

        var protectedConfig = configProtector.CreateProtectedClone(config);
        var json = JsonSerializer.Serialize(protectedConfig, WriteOptions);
        await File.WriteAllTextAsync(configFilePath, json);
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
