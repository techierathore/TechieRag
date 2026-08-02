using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services;
using TechieRag;
using Xunit;

namespace TechieDesk.Tests;

/// <summary>
/// REQ-NFR-004 / REQ-NFR-012 / BRD-95: provider API keys AND the vector-store and persistence
/// connection strings are encrypted at rest in <c>techierag-config.json</c>. These are outbound
/// credentials the app must replay to the LLM / embedding / vector-store providers and to the
/// databases, so the protection is reversible encryption (ASP.NET Core Data Protection), never a
/// one-way hash.
/// </summary>
public sealed class ConfigEncryptionTests
{
    private const string EmbeddingKey = "sk-embedding-VERYSECRET-0001";
    private const string VectorStoreKey = "qdrant-VERYSECRET-0002";
    private const string LlmKey = "sk-llm-VERYSECRET-0003";
    private const string FallbackKey = "sk-fallback-VERYSECRET-0004";
    private const string RerankKey = "co-rerank-VERYSECRET-0005";

    /// <summary>The password embedded in the DSNs below — the string that must never reach disk.</summary>
    private const string DsnPassword = "pgPa55-VERYSECRET-0006";

    /// <summary>A PgVector DSN of exactly the shape REQ-NFR-012 was raised about.</summary>
    private const string VectorStoreDsn =
        "Host=db.internal;Port=5432;Database=techierag;Username=rag;Password=" + DsnPassword;

    /// <summary>A Postgres DSN for the relational persistence layer.</summary>
    private const string PersistenceDsn =
        "Host=db.internal;Port=5432;Database=techiedesk;Username=desk;Password=" + DsnPassword;

    /// <summary>
    /// The number of credential fields <see cref="BuildConfigWithKeys"/> populates: five provider
    /// API keys plus the two connection strings added by REQ-NFR-012.
    /// </summary>
    private const int ProtectedFieldCount = 7;

    /// <summary>
    /// A configuration saved with provider keys reloads with exactly those keys, so the app can
    /// still replay them to the providers.
    /// </summary>
    [Fact]
    public async Task SavedApiKeysRoundTripThroughSaveAndLoad()
    {
        using var host = new ConfigEncryptionTestHost();
        await host.CreateConfigService().SaveConfigAsync(BuildConfigWithKeys());

        var reloaded = await host.CreateConfigService().LoadConfigAsync();

        Assert.Equal(EmbeddingKey, reloaded.Embedding.ApiKey);
        Assert.Equal(VectorStoreKey, reloaded.VectorStore.ApiKey);
        Assert.Equal(LlmKey, reloaded.Llm.ApiKey);
        Assert.Equal(FallbackKey, reloaded.LlmFallback!.ApiKey);
        Assert.Equal(RerankKey, reloaded.Rerank.ApiKey);
    }

    /// <summary>
    /// The security claim itself: the bytes on disk contain no cleartext key material, and every
    /// credential carries the <c>enc:v1:</c> marker.
    /// </summary>
    [Fact]
    public async Task SavedFileHoldsNoCleartextKeyMaterial()
    {
        using var host = new ConfigEncryptionTestHost();

        await host.CreateConfigService().SaveConfigAsync(BuildConfigWithKeys());

        var raw = host.ReadRawConfigFile();
        Assert.DoesNotContain(EmbeddingKey, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(VectorStoreKey, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(LlmKey, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(FallbackKey, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(RerankKey, raw, StringComparison.Ordinal);
        Assert.Equal(ProtectedFieldCount, CountOccurrences(raw, TechieRagConfigProtector.EncryptedPrefix));
    }

    /// <summary>
    /// An existing install whose file holds cleartext keys keeps working: the key is used as-is and
    /// the file is transparently rewritten encrypted on the next read.
    /// </summary>
    [Fact]
    public async Task LegacyCleartextConfigIsUpgradedOnRead()
    {
        using var host = new ConfigEncryptionTestHost();
        await File.WriteAllTextAsync(host.ConfigFilePath, SerializeCleartext(BuildConfigWithKeys()));

        var loaded = await host.CreateConfigService().LoadConfigAsync();

        Assert.Equal(EmbeddingKey, loaded.Embedding.ApiKey);
        Assert.Equal(LlmKey, loaded.Llm.ApiKey);
        var raw = host.ReadRawConfigFile();
        Assert.DoesNotContain(EmbeddingKey, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(LlmKey, raw, StringComparison.Ordinal);
        Assert.Contains(TechieRagConfigProtector.EncryptedPrefix, raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ciphertext that cannot be decrypted (lost or rotated key ring, hand-edited file) is dropped
    /// from the loaded configuration, does not throw, and above all does not destroy the saved file.
    /// </summary>
    [Fact]
    public async Task UndecryptableValueFailsSafeAndLeavesFileIntact()
    {
        using var host = new ConfigEncryptionTestHost();
        var corrupt = SerializeCleartext(BuildConfigWithKeys())
            .Replace(EmbeddingKey, TechieRagConfigProtector.EncryptedPrefix + "not-a-real-payload", StringComparison.Ordinal);
        await File.WriteAllTextAsync(host.ConfigFilePath, corrupt);

        var loaded = await host.CreateConfigService().LoadConfigAsync();

        Assert.Null(loaded.Embedding.ApiKey);
        Assert.Equal(LlmKey, loaded.Llm.ApiKey);
        Assert.Equal(corrupt, host.ReadRawConfigFile());
    }

    /// <summary>
    /// A configuration with no keys at all saves and reloads cleanly, so local-only setups
    /// (Ollama + SqliteVec) keep working with nothing to decrypt but their own store path.
    /// </summary>
    /// <remarks>
    /// An empty or absent value is still never encrypted — there is nothing to protect and an
    /// <c>enc:v1:</c> marker over an empty string would only be noise. Since REQ-NFR-012 the default
    /// SqliteVec <c>ConnectionString</c> is a populated field, so exactly one marker is expected
    /// here: the local store path goes through the same path as a PgVector DSN rather than being
    /// judged by its contents. No <i>key</i> material is encrypted in this configuration.
    /// <para>
    /// REQ-FN-048: the store path that round-trips is the ABSOLUTE one inside the data directory, not
    /// the relative <c>Data Source=techierag.db</c> default <see cref="TechieRagConfig"/> ships with.
    /// A relative path resolves against the process working directory, which on the desktop head is
    /// the signed <c>.app</c> bundle root.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EmptyAndAbsentKeysAreLeftUnencrypted()
    {
        using var host = new ConfigEncryptionTestHost();
        var config = new TechieRagConfig();
        config.Embedding.ApiKey = string.Empty;

        await host.CreateConfigService().SaveConfigAsync(config);
        var reloaded = await host.CreateConfigService().LoadConfigAsync();

        Assert.True(string.IsNullOrEmpty(reloaded.Embedding.ApiKey));
        Assert.Null(reloaded.Llm.ApiKey);
        Assert.Null(reloaded.Persistence.ConnectionString);
        Assert.Equal(
            TechieDeskDb.DataDirectory.VectorDbConnectionString(
                TechieDeskDb.DataDirectory.Resolve(host.DataDirectoryPath)),
            reloaded.VectorStore.ConnectionString);
        Assert.Equal(
            1,
            CountOccurrences(host.ReadRawConfigFile(), TechieRagConfigProtector.EncryptedPrefix));
    }

    /// <summary>
    /// The key-ring persistence check: a value encrypted by one process decrypts in a completely new
    /// Data Protection provider built over the same on-disk key ring — i.e. it survives a restart.
    /// </summary>
    [Fact]
    public void EncryptedKeysSurviveAKeyRingRestart()
    {
        using var host = new ConfigEncryptionTestHost();
        var beforeRestart = BuildProtector(host).CreateProtectedClone(BuildConfigWithKeys());

        var afterRestart = BuildProtector(host);
        var reopened = Clone(beforeRestart);
        var upgradeNeeded = afterRestart.RevealSecrets(reopened);

        Assert.False(upgradeNeeded);
        Assert.Equal(LlmKey, reopened.Llm.ApiKey);
        Assert.Equal(EmbeddingKey, reopened.Embedding.ApiKey);
    }

    /// <summary>
    /// Saving must not mutate the caller's instance — the Settings page keeps the cleartext key
    /// bound to its input rather than showing ciphertext after a save.
    /// </summary>
    [Fact]
    public async Task SavingDoesNotMutateTheCallersConfiguration()
    {
        using var host = new ConfigEncryptionTestHost();
        var config = BuildConfigWithKeys();

        await host.CreateConfigService().SaveConfigAsync(config);

        Assert.Equal(LlmKey, config.Llm.ApiKey);
        Assert.Equal(EmbeddingKey, config.Embedding.ApiKey);
        Assert.Equal(VectorStoreDsn, config.VectorStore.ConnectionString);
        Assert.Equal(PersistenceDsn, config.Persistence.ConnectionString);
    }

    /// <summary>
    /// Re-saving an already encrypted configuration does not double-encrypt it.
    /// </summary>
    [Fact]
    public void AlreadyEncryptedValuesAreNotEncryptedTwice()
    {
        using var host = new ConfigEncryptionTestHost();
        var protector = BuildProtector(host);

        var once = protector.CreateProtectedClone(BuildConfigWithKeys());
        var twice = protector.CreateProtectedClone(once);

        Assert.Equal(once.Llm.ApiKey, twice.Llm.ApiKey);
        Assert.False(protector.RevealSecrets(twice));
        Assert.Equal(LlmKey, twice.Llm.ApiKey);
    }

    /// <summary>
    /// REQ-NFR-012, the security claim: a <c>Password=</c>-bearing DSN never appears in plaintext on
    /// disk, and still round-trips exactly, because the app has to replay it to open the connection.
    /// </summary>
    [Fact]
    public async Task ConnectionStringPasswordNeverReachesDiskInPlaintext()
    {
        using var host = new ConfigEncryptionTestHost();

        await host.CreateConfigService().SaveConfigAsync(BuildConfigWithKeys());

        var raw = host.ReadRawConfigFile();
        Assert.DoesNotContain(DsnPassword, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(VectorStoreDsn, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(PersistenceDsn, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", raw, StringComparison.OrdinalIgnoreCase);

        var reloaded = await host.CreateConfigService().LoadConfigAsync();
        Assert.Equal(VectorStoreDsn, reloaded.VectorStore.ConnectionString);
        Assert.Equal(PersistenceDsn, reloaded.Persistence.ConnectionString);
    }

    /// <summary>
    /// The persisted connection strings carry the same <c>enc:v1:</c> envelope as the API keys — one
    /// mechanism, not two.
    /// </summary>
    [Fact]
    public void ConnectionStringsUseTheSameEnvelopeAsApiKeys()
    {
        using var host = new ConfigEncryptionTestHost();

        var protectedClone = BuildProtector(host).CreateProtectedClone(BuildConfigWithKeys());

        Assert.StartsWith(
            TechieRagConfigProtector.EncryptedPrefix,
            protectedClone.VectorStore.ConnectionString,
            StringComparison.Ordinal);
        Assert.StartsWith(
            TechieRagConfigProtector.EncryptedPrefix,
            protectedClone.Persistence.ConnectionString,
            StringComparison.Ordinal);
        Assert.True(TechieRagConfigProtector.IsProtected(protectedClone.VectorStore.ConnectionString));
        Assert.True(TechieRagConfigProtector.IsProtected(protectedClone.Persistence.ConnectionString));
    }

    /// <summary>
    /// An existing install whose file holds cleartext connection strings is upgraded transparently
    /// on the first save, and the password is gone from the file afterwards.
    /// </summary>
    /// <remarks>
    /// The upgrade is driven by the same read-then-rewrite the API keys use, so an install that has
    /// been sitting on a cleartext PgVector DSN since before REQ-NFR-012 heals itself the next time
    /// the configuration is touched — without the operator re-entering anything.
    /// </remarks>
    [Fact]
    public async Task LegacyCleartextConnectionStringsAreUpgradedOnFirstSave()
    {
        using var host = new ConfigEncryptionTestHost();
        await File.WriteAllTextAsync(host.ConfigFilePath, SerializeCleartext(BuildConfigWithKeys()));
        Assert.Contains(DsnPassword, host.ReadRawConfigFile(), StringComparison.Ordinal);

        var loaded = await host.CreateConfigService().LoadConfigAsync();

        // Used as-is in memory, so nothing breaks for the operator mid-upgrade...
        Assert.Equal(VectorStoreDsn, loaded.VectorStore.ConnectionString);
        Assert.Equal(PersistenceDsn, loaded.Persistence.ConnectionString);

        // ...and the file no longer holds the password.
        var raw = host.ReadRawConfigFile();
        Assert.DoesNotContain(DsnPassword, raw, StringComparison.Ordinal);
        Assert.Equal(ProtectedFieldCount, CountOccurrences(raw, TechieRagConfigProtector.EncryptedPrefix));
    }

    /// <summary>
    /// Repeated ordinary saves never regress a protected connection string back to cleartext, which
    /// is the other half of "transparently upgraded, never silently re-written cleartext".
    /// </summary>
    [Fact]
    public async Task RepeatedSavesNeverRewriteAConnectionStringInCleartext()
    {
        using var host = new ConfigEncryptionTestHost();
        await host.CreateConfigService().SaveConfigAsync(BuildConfigWithKeys());

        for (var i = 0; i < 3; i++)
        {
            var service = host.CreateConfigService();
            var config = await service.LoadConfigAsync();
            config.Processing.DefaultChunkSize = 100 + i;
            await service.SaveConfigAsync(config);
            Assert.DoesNotContain(DsnPassword, host.ReadRawConfigFile(), StringComparison.Ordinal);
        }

        var reloaded = await host.CreateConfigService().LoadConfigAsync();
        Assert.Equal(VectorStoreDsn, reloaded.VectorStore.ConnectionString);
        Assert.Equal(PersistenceDsn, reloaded.Persistence.ConnectionString);
    }

    /// <summary>
    /// A connection string that cannot be decrypted degrades exactly as an API key does: the field is
    /// cleared, nothing throws, and the operator's saved file is left untouched.
    /// </summary>
    /// <remarks>
    /// <c>VectorStore.ConnectionString</c> is non-nullable, so its fail-safe clear is
    /// <see cref="string.Empty"/> rather than null — the same outcome expressed in the property's own
    /// type.
    /// </remarks>
    [Fact]
    public async Task UndecryptableConnectionStringFailsSafeAndLeavesFileIntact()
    {
        using var host = new ConfigEncryptionTestHost();
        var corrupt = SerializeCleartext(BuildConfigWithKeys())
            .Replace(VectorStoreDsn, TechieRagConfigProtector.EncryptedPrefix + "not-a-real-payload", StringComparison.Ordinal);
        await File.WriteAllTextAsync(host.ConfigFilePath, corrupt);

        var loaded = await host.CreateConfigService().LoadConfigAsync();

        Assert.Equal(string.Empty, loaded.VectorStore.ConnectionString);
        Assert.Equal(PersistenceDsn, loaded.Persistence.ConnectionString);
        Assert.Equal(corrupt, host.ReadRawConfigFile());
    }

    /// <summary>
    /// The key-ring persistence check for connection strings: a DSN encrypted by one process decrypts
    /// in a brand new Data Protection provider over the same on-disk key ring, i.e. across a restart.
    /// </summary>
    [Fact]
    public void EncryptedConnectionStringsSurviveAKeyRingRestart()
    {
        using var host = new ConfigEncryptionTestHost();
        var beforeRestart = BuildProtector(host).CreateProtectedClone(BuildConfigWithKeys());

        var reopened = Clone(beforeRestart);
        var upgradeNeeded = BuildProtector(host).RevealSecrets(reopened);

        Assert.False(upgradeNeeded);
        Assert.Equal(VectorStoreDsn, reopened.VectorStore.ConnectionString);
        Assert.Equal(PersistenceDsn, reopened.Persistence.ConnectionString);
    }

    private static TechieRagConfigProtector BuildProtector(ConfigEncryptionTestHost host) =>
        new(host.CreateProvider(), NullLogger<TechieRagConfigProtector>.Instance);

    private static TechieRagConfig BuildConfigWithKeys() => new()
    {
        Embedding = new EmbeddingConfig { Source = EmbeddingSource.OpenAI, ApiKey = EmbeddingKey },
        // REQ-NFR-012: a PgVector store is the case that embeds Password= in the connection string.
        VectorStore = new VectorStoreConfig
        {
            Type = VectorStoreType.PgVector,
            ApiKey = VectorStoreKey,
            ConnectionString = VectorStoreDsn
        },
        Persistence = new PersistenceConfig
        {
            Provider = StoreProvider.Postgres,
            ConnectionString = PersistenceDsn
        },
        // REQ-UI-043: the providers carry every field their source requires, because saving a
        // half-configured provider is now refused outright.
        Llm = new LlmConfig
        {
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.openai.com",
            Model = "gpt-4o",
            ApiKey = LlmKey
        },
        LlmFallback = new LlmConfig
        {
            Source = LlmSource.Anthropic,
            Model = "claude-sonnet-4-5-20250929",
            ApiKey = FallbackKey
        },
        Rerank = new RerankConfig { Enabled = true, Source = RerankSource.Cohere, ApiKey = RerankKey }
    };

    private static string SerializeCleartext(TechieRagConfig config) =>
        JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

    private static TechieRagConfig Clone(TechieRagConfig config) =>
        JsonSerializer.Deserialize<TechieRagConfig>(
            SerializeCleartext(config),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
