using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services;
using TechieDesk.Services.Auth;
using TechieRag;
using Xunit;

namespace TechieDesk.Tests;

/// <summary>
/// REQ-FN-039 / REQ-NFR-004b: with the OS credential store available, provider API keys are stored
/// in it and <c>techierag-config.json</c> holds only an opaque reference — no ciphertext, no
/// cleartext. Without a durable store, the REQ-NFR-004b Data Protection scheme still applies, so the
/// encryption-at-rest property strengthens and never regresses.
/// </summary>
public sealed class ProviderSecretStoreTests
{
    private const string LlmKey = "sk-llm-VERYSECRET-0003";
    private const string EmbeddingKey = "sk-embedding-VERYSECRET-0001";
    private const string DsnPassword = "pgPa55-VERYSECRET-0006";
    private const string Dsn = "Host=db.internal;Database=techierag;Username=rag;Password=" + DsnPassword;

    /// <summary>
    /// The security claim: with a durable store, the protected configuration carries no key material
    /// at all — the secret is in the OS store and the file names only the field.
    /// </summary>
    [Fact]
    public void ProviderKeysGoToTheOsStoreAndLeaveNoCiphertextBehind()
    {
        using var host = new ConfigEncryptionTestHost();
        var secrets = new DurableTestSecretStore();
        var protector = BuildProtector(host, secrets);

        var protectedClone = protector.CreateProtectedClone(BuildConfigWithKeys());

        Assert.Equal(TechieRagConfigProtector.SecretReferencePrefix + "Llm:ApiKey", protectedClone.Llm.ApiKey);
        Assert.Equal(
            TechieRagConfigProtector.SecretReferencePrefix + "Embedding:ApiKey",
            protectedClone.Embedding.ApiKey);

        var serialized = System.Text.Json.JsonSerializer.Serialize(protectedClone);
        Assert.DoesNotContain(LlmKey, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(EmbeddingKey, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(TechieRagConfigProtector.EncryptedPrefix, serialized, StringComparison.Ordinal);

        Assert.Equal(LlmKey, secrets.Read(TechieRagConfigProtector.SecretKeyPrefix + "Llm:ApiKey"));
    }

    /// <summary>
    /// The app must still be able to replay the keys to the providers, so a stored reference resolves
    /// back to the exact key — including across a restart, which is what the OS store buys.
    /// </summary>
    [Fact]
    public void StoredProviderKeysRoundTripAcrossARestart()
    {
        using var host = new ConfigEncryptionTestHost();
        var secrets = new DurableTestSecretStore();
        var saved = BuildProtector(host, secrets).CreateProtectedClone(BuildConfigWithKeys());

        // A new protector over a new Data Protection key ring: nothing but the credential store
        // carries over, which is the point.
        var reopened = Clone(saved);
        var upgradeNeeded = BuildProtector(host, secrets).RevealSecrets(reopened);

        Assert.False(upgradeNeeded);
        Assert.Equal(LlmKey, reopened.Llm.ApiKey);
        Assert.Equal(EmbeddingKey, reopened.Embedding.ApiKey);
    }

    /// <summary>
    /// An existing install's <c>enc:v1:</c> values keep working and are flagged for migration into
    /// the OS store, so upgrading strengthens protection rather than losing an operator's keys.
    /// </summary>
    [Fact]
    public void ExistingEncryptedKeysAreReadableAndFlaggedForMigration()
    {
        using var host = new ConfigEncryptionTestHost();
        var provider = host.CreateProvider();
        var legacy = new TechieRagConfigProtector(provider, NullLogger<TechieRagConfigProtector>.Instance)
            .CreateProtectedClone(BuildConfigWithKeys());
        Assert.StartsWith(TechieRagConfigProtector.EncryptedPrefix, legacy.Llm.ApiKey, StringComparison.Ordinal);

        var secrets = new DurableTestSecretStore();
        var upgraded = Clone(legacy);
        var upgradeNeeded = new TechieRagConfigProtector(
            provider, NullLogger<TechieRagConfigProtector>.Instance, secrets).RevealSecrets(upgraded);

        Assert.True(upgradeNeeded);
        Assert.Equal(LlmKey, upgraded.Llm.ApiKey);
    }

    /// <summary>
    /// An in-memory (non-durable) store must NOT be used: a reference it could not resolve after a
    /// restart would lose the operator's key, so the Data Protection scheme stays in charge.
    /// </summary>
    [Fact]
    public void ANonDurableStoreFallsBackToEncryptionAtRest()
    {
        using var host = new ConfigEncryptionTestHost();

        var protectedClone = BuildProtector(host, new EphemeralSecretStore())
            .CreateProtectedClone(BuildConfigWithKeys());

        Assert.StartsWith(TechieRagConfigProtector.EncryptedPrefix, protectedClone.Llm.ApiKey, StringComparison.Ordinal);
        Assert.DoesNotContain(LlmKey, protectedClone.Llm.ApiKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-NFR-012: the connection strings degrade EXACTLY as the API keys do. With a durable store
    /// they become an <c>enc:v2:</c> reference; without one they fall back to <c>enc:v1:</c>
    /// ciphertext. Either way the embedded password is off the file.
    /// </summary>
    [Fact]
    public void ConnectionStringsFollowTheSameSchemeAsApiKeys()
    {
        using var host = new ConfigEncryptionTestHost();
        var secrets = new DurableTestSecretStore();

        var stored = BuildProtector(host, secrets).CreateProtectedClone(BuildConfigWithDsn());
        Assert.Equal(
            TechieRagConfigProtector.SecretReferencePrefix + "VectorStore:ConnectionString",
            stored.VectorStore.ConnectionString);
        Assert.Equal(Dsn, secrets.Read(TechieRagConfigProtector.SecretKeyPrefix + "VectorStore:ConnectionString"));

        var encrypted = BuildProtector(host, new EphemeralSecretStore())
            .CreateProtectedClone(BuildConfigWithDsn());
        Assert.StartsWith(
            TechieRagConfigProtector.EncryptedPrefix,
            encrypted.VectorStore.ConnectionString,
            StringComparison.Ordinal);

        foreach (var serialized in new[]
                 {
                     System.Text.Json.JsonSerializer.Serialize(stored),
                     System.Text.Json.JsonSerializer.Serialize(encrypted)
                 })
        {
            Assert.DoesNotContain(DsnPassword, serialized, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A reference whose OS-store entry has gone (a wiped keychain) fails safe: the field is dropped,
    /// nothing throws, and the caller is told NOT to rewrite the file over the operator's data.
    /// </summary>
    [Fact]
    public void AMissingStoreEntryFailsSafeAndBlocksTheRewrite()
    {
        using var host = new ConfigEncryptionTestHost();
        var secrets = new DurableTestSecretStore();
        var saved = BuildProtector(host, secrets).CreateProtectedClone(BuildConfigWithKeys());
        secrets.Delete(TechieRagConfigProtector.SecretKeyPrefix + "Llm:ApiKey");

        var reopened = Clone(saved);
        var upgradeNeeded = BuildProtector(host, secrets).RevealSecrets(reopened);

        Assert.False(upgradeNeeded);
        Assert.Null(reopened.Llm.ApiKey);
        Assert.Equal(EmbeddingKey, reopened.Embedding.ApiKey);
    }

    /// <summary>Clearing a key in Settings also removes it from the OS credential store.</summary>
    [Fact]
    public void ClearingAKeyRemovesItFromTheStore()
    {
        using var host = new ConfigEncryptionTestHost();
        var secrets = new DurableTestSecretStore();
        var protector = BuildProtector(host, secrets);
        protector.CreateProtectedClone(BuildConfigWithKeys());

        var cleared = BuildConfigWithKeys();
        cleared.Llm.ApiKey = null;
        protector.CreateProtectedClone(cleared);

        Assert.Null(secrets.Read(TechieRagConfigProtector.SecretKeyPrefix + "Llm:ApiKey"));
        Assert.Equal(EmbeddingKey, secrets.Read(TechieRagConfigProtector.SecretKeyPrefix + "Embedding:ApiKey"));
    }

    private static TechieRagConfigProtector BuildProtector(
        ConfigEncryptionTestHost host, ISecretStore secrets) =>
        new(host.CreateProvider(), NullLogger<TechieRagConfigProtector>.Instance, secrets);

    private static TechieRagConfig Clone(TechieRagConfig config) =>
        System.Text.Json.JsonSerializer.Deserialize<TechieRagConfig>(
            System.Text.Json.JsonSerializer.Serialize(config))!;

    /// <summary>A configuration whose vector store is reached over a password-bearing DSN.</summary>
    private static TechieRagConfig BuildConfigWithDsn()
    {
        var config = BuildConfigWithKeys();
        config.VectorStore = new VectorStoreConfig
        {
            Type = VectorStoreType.PgVector,
            ConnectionString = Dsn
        };
        return config;
    }

    private static TechieRagConfig BuildConfigWithKeys() => new()
    {
        Embedding = new EmbeddingConfig { Source = EmbeddingSource.OpenAI, ApiKey = EmbeddingKey },
        Llm = new LlmConfig
        {
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.openai.com",
            Model = "gpt-4o",
            ApiKey = LlmKey
        }
    };

    /// <summary>
    /// Stands in for Keychain / the Windows Credential Manager: it reports itself durable, which is
    /// the flag the protector keys its scheme off.
    /// </summary>
    private sealed class DurableTestSecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public bool IsDurable => true;

        public string? Read(string key) => values.TryGetValue(key, out var value) ? value : null;

        public void Write(string key, string value) => values[key] = value;

        public bool Delete(string key) => values.Remove(key);
    }
}
