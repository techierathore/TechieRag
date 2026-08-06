using TechieRag;
using TechieDesk.Services;
using Xunit;

namespace TechieDesk.Tests.Settings;

/// <summary>
/// Tests for the section-scoped configuration save that fixes the lost update (REQ-NFR-004).
/// </summary>
/// <remarks>
/// <para><b>The defect these reproduce.</b> Four screens write the one <c>techierag-config.json</c>
/// through <see cref="TechieRagConfigService"/>, each loading the whole document when it opens and
/// saving the whole document back. Two pairs overlap — <c>Embedding</c>/<c>VectorStore</c> between
/// App Settings and RAG Configuration, and <c>Llm</c> between App Settings and LLM Settings — so the
/// second screen to save silently reverted the first, with a success toast and no log line.</para>
/// <para>Each test uses a SEPARATE service instance per screen, which is the point: one instance
/// would share the in-memory cache and hide exactly the staleness being tested. A separate instance
/// with its own empty cache is the honest model of two screens that were each opened at a different
/// time.</para>
/// </remarks>
public class ConfigSectionMergeTests
{
    /// <summary>Sections App Settings owns.</summary>
    private const TechieRagConfigSections AppSettingsOwns =
        TechieRagConfigSections.Embedding | TechieRagConfigSections.VectorStore | TechieRagConfigSections.Llm;

    /// <summary>Sections RAG Configuration owns.</summary>
    private const TechieRagConfigSections RagConfigOwns =
        TechieRagConfigSections.Embedding | TechieRagConfigSections.VectorStore
        | TechieRagConfigSections.Processing | TechieRagConfigSections.Telemetry;

    /// <summary>Sections LLM Settings owns.</summary>
    private const TechieRagConfigSections LlmSettingsOwns =
        TechieRagConfigSections.Llm | TechieRagConfigSections.LlmFallback | TechieRagConfigSections.Prompt
        | TechieRagConfigSections.Resilience | TechieRagConfigSections.UsageTracking;

    /// <summary>Writes a starting configuration to the sandbox, as a configured install would have.</summary>
    private static async Task SeedAsync(ConfigEncryptionTestHost host)
    {
        var seed = new TechieRagConfig();
        seed.Embedding.Model = "seed-embed";
        seed.VectorStore.Type = VectorStoreType.SqliteVec;
        seed.Llm.Model = "seed-llm";
        seed.Processing.DefaultChunkSize = 500;
        await host.CreateConfigService().SaveConfigAsync(seed, TechieRagConfigSections.All);
    }

    /// <summary>
    /// THE REGRESSION: LLM Settings saving its own screen must not revert App Settings' embedding.
    /// </summary>
    /// <remarks>
    /// Before the fix this failed — the second save wrote its stale <c>Embedding</c> back over the
    /// first, so the assertion below saw "seed-embed" instead of "changed-by-app-settings".
    /// </remarks>
    [Fact]
    public async Task SavingLlmSettingsDoesNotRevertAnEmbeddingChangeFromAppSettings()
    {
        using var host = new ConfigEncryptionTestHost();
        await SeedAsync(host);

        // App Settings opens, changes the embedding model, saves.
        var appSettings = host.CreateConfigService();
        var appConfig = await appSettings.LoadConfigAsync();
        appConfig.Embedding.Model = "changed-by-app-settings";
        await appSettings.SaveConfigAsync(appConfig, AppSettingsOwns);

        // LLM Settings was opened BEFORE that change, so its copy still says "seed-embed".
        var llmSettings = host.CreateConfigService();
        var llmConfig = await llmSettings.LoadConfigAsync();
        llmConfig.Embedding.Model = "seed-embed";
        llmConfig.Llm.Model = "changed-by-llm-settings";
        await llmSettings.SaveConfigAsync(llmConfig, LlmSettingsOwns);

        var onDisk = await host.CreateConfigService().LoadConfigAsync();
        Assert.Equal("changed-by-app-settings", onDisk.Embedding.Model);
        Assert.Equal("changed-by-llm-settings", onDisk.Llm.Model);
    }

    /// <summary>
    /// THE REGRESSION, other direction: App Settings must not revert LLM Settings' prompt options.
    /// </summary>
    [Fact]
    public async Task SavingAppSettingsDoesNotRevertSectionsItDoesNotOwn()
    {
        using var host = new ConfigEncryptionTestHost();
        await SeedAsync(host);

        var llmSettings = host.CreateConfigService();
        var llmConfig = await llmSettings.LoadConfigAsync();
        llmConfig.Resilience.MaxRetries = 9;
        await llmSettings.SaveConfigAsync(llmConfig, LlmSettingsOwns);

        var appSettings = host.CreateConfigService();
        var appConfig = await appSettings.LoadConfigAsync();
        appConfig.Resilience.MaxRetries = 3;          // stale copy
        appConfig.Embedding.Model = "app-settings-embed";
        await appSettings.SaveConfigAsync(appConfig, AppSettingsOwns);

        var onDisk = await host.CreateConfigService().LoadConfigAsync();
        Assert.Equal(9, onDisk.Resilience.MaxRetries);
        Assert.Equal("app-settings-embed", onDisk.Embedding.Model);
    }

    /// <summary>
    /// Verifies RAG Configuration keeps its own sections while leaving the LLM stack alone.
    /// </summary>
    [Fact]
    public async Task RagConfigurationWritesItsOwnSectionsAndLeavesTheLlmStackAlone()
    {
        using var host = new ConfigEncryptionTestHost();
        await SeedAsync(host);

        var llmSettings = host.CreateConfigService();
        var llmConfig = await llmSettings.LoadConfigAsync();
        llmConfig.Llm.Model = "llm-owned";
        await llmSettings.SaveConfigAsync(llmConfig, LlmSettingsOwns);

        var ragConfig = host.CreateConfigService();
        var rag = await ragConfig.LoadConfigAsync();
        rag.Llm.Model = "stale";                       // stale copy of a section it does not own
        rag.Processing.DefaultChunkSize = 1234;
        rag.EnableTelemetry = false;
        await ragConfig.SaveConfigAsync(rag, RagConfigOwns);

        var onDisk = await host.CreateConfigService().LoadConfigAsync();
        Assert.Equal("llm-owned", onDisk.Llm.Model);
        Assert.Equal(1234, onDisk.Processing.DefaultChunkSize);
        Assert.False(onDisk.EnableTelemetry);
    }

    /// <summary>
    /// Verifies two screens editing the SAME section still resolve last-writer-wins, deliberately.
    /// </summary>
    /// <remarks>
    /// This is the documented limit of the fix, asserted so nobody mistakes it for a transaction:
    /// the merge narrows a lost update to sections a screen does not own. When both screens really
    /// are editing the same field, the later save winning is the honest outcome.
    /// </remarks>
    [Fact]
    public async Task TwoScreensEditingTheSameSectionStillResolveLastWriterWins()
    {
        using var host = new ConfigEncryptionTestHost();
        await SeedAsync(host);

        var first = host.CreateConfigService();
        var firstConfig = await first.LoadConfigAsync();
        firstConfig.Embedding.Model = "first";
        await first.SaveConfigAsync(firstConfig, AppSettingsOwns);

        var second = host.CreateConfigService();
        var secondConfig = await second.LoadConfigAsync();
        secondConfig.Embedding.Model = "second";
        await second.SaveConfigAsync(secondConfig, RagConfigOwns);

        var onDisk = await host.CreateConfigService().LoadConfigAsync();
        Assert.Equal("second", onDisk.Embedding.Model);
    }

    /// <summary>Verifies a whole-document save still works, which the first-run wizard relies on.</summary>
    [Fact]
    public async Task SavingAllSectionsWritesTheWholeDocument()
    {
        using var host = new ConfigEncryptionTestHost();
        await SeedAsync(host);

        var service = host.CreateConfigService();
        var config = new TechieRagConfig();
        config.Embedding.Model = "wizard-embed";
        config.Llm.Model = "wizard-llm";
        await service.SaveConfigAsync(config, TechieRagConfigSections.All);

        var onDisk = await host.CreateConfigService().LoadConfigAsync();
        Assert.Equal("wizard-embed", onDisk.Embedding.Model);
        Assert.Equal("wizard-llm", onDisk.Llm.Model);
    }

    /// <summary>Verifies a save owning nothing is refused as a programming error.</summary>
    [Fact]
    public async Task SavingWithNoSectionsIsRefused()
    {
        using var host = new ConfigEncryptionTestHost();
        var service = host.CreateConfigService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SaveConfigAsync(new TechieRagConfig(), TechieRagConfigSections.None));
    }

    /// <summary>Verifies the merge does not double-encrypt an API key it copies through.</summary>
    /// <remarks>
    /// The merge reads from disk (which reveals secrets) and the write re-protects them. If that
    /// symmetry ever broke, a key belonging to a section the caller does NOT own would be re-encrypted
    /// on every unrelated save and become unreadable — a silent, cumulative corruption.
    /// </remarks>
    [Fact]
    public async Task MergingDoesNotDoubleEncryptAKeyItOnlyPassesThrough()
    {
        using var host = new ConfigEncryptionTestHost();

        var seed = new TechieRagConfig();
        seed.Embedding.ApiKey = "embedding-secret";
        seed.Llm.ApiKey = "llm-secret";
        await host.CreateConfigService().SaveConfigAsync(seed, TechieRagConfigSections.All);

        // Save a section that owns NEITHER key, three times over.
        for (var i = 0; i < 3; i++)
        {
            var service = host.CreateConfigService();
            var config = await service.LoadConfigAsync();
            config.Processing.DefaultChunkSize = 100 + i;
            await service.SaveConfigAsync(config, TechieRagConfigSections.Processing);
        }

        var onDisk = await host.CreateConfigService().LoadConfigAsync();
        Assert.Equal("embedding-secret", onDisk.Embedding.ApiKey);
        Assert.Equal("llm-secret", onDisk.Llm.ApiKey);
    }

    /// <summary>
    /// REQ-NFR-012: the same pass-through symmetry holds for the connection strings, and no save by a
    /// screen that does not own them ever puts the password back on disk.
    /// </summary>
    /// <remarks>
    /// This is the interaction worth pinning down. The merge re-reads from DISK rather than from the
    /// cache, so a connection string belonging to an unowned section makes a full
    /// decrypt-then-re-encrypt round trip on every unrelated save. If that symmetry broke it would
    /// show up either as a double-encrypted DSN nobody can read any more, or — far worse — as the
    /// password reappearing in cleartext in a file some other screen wrote. Both are asserted, on
    /// every iteration rather than only at the end, so a regression names the save that caused it.
    /// </remarks>
    [Fact]
    public async Task MergingPreservesConnectionStringsWithoutEverWritingThemInCleartext()
    {
        using var host = new ConfigEncryptionTestHost();
        const string password = "pgPa55-VERYSECRET-0006";
        const string vectorDsn = "Host=db.internal;Database=techierag;Username=rag;Password=" + password;
        const string persistenceDsn = "Host=db.internal;Database=techiedesk;Username=desk;Password=" + password;

        var seed = new TechieRagConfig();
        seed.VectorStore.Type = VectorStoreType.PgVector;
        seed.VectorStore.ConnectionString = vectorDsn;
        seed.Persistence.Provider = StoreProvider.Postgres;
        seed.Persistence.ConnectionString = persistenceDsn;
        await host.CreateConfigService().SaveConfigAsync(seed, TechieRagConfigSections.All);

        // LLM Settings owns neither section, so both DSNs are only ever passed through.
        for (var i = 0; i < 3; i++)
        {
            var service = host.CreateConfigService();
            var config = await service.LoadConfigAsync();
            config.Llm.Model = $"llm-{i}";
            await service.SaveConfigAsync(config, LlmSettingsOwns);
            Assert.DoesNotContain(password, host.ReadRawConfigFile(), StringComparison.Ordinal);
        }

        var onDisk = await host.CreateConfigService().LoadConfigAsync();
        Assert.Equal(vectorDsn, onDisk.VectorStore.ConnectionString);
        Assert.Equal(persistenceDsn, onDisk.Persistence.ConnectionString);
        Assert.Equal("llm-2", onDisk.Llm.Model);
    }
}
