using TechieDesk.Services;
using TechieDeskDb;
using TechieRag;
using Xunit;

namespace TechieDesk.Tests.Settings;

/// <summary>
/// Pins down the round trip behind the LLM Settings "Save &amp; apply" button (REQ-FN-052, BRD-9).
/// </summary>
/// <remarks>
/// <para><b>The defect these reproduce.</b> Selecting a provider on <c>/llm-settings</c> and pressing
/// Save &amp; apply left the screen reading back as that provider while
/// <c>techierag-config.json</c> — the file <c>TechieRagManager</c> builds the RAG instance from — still
/// held <c>llm.source: 0</c>, so chat kept answering "No LLM provider is configured". The page and the
/// RAG instance genuinely read two different stores.</para>
/// <para><b>What the second store turned out to be.</b> Not a second file and not a database table:
/// <see cref="TechieRagConfigService.LoadConfigAsync"/> handed every screen the SAME cached
/// <see cref="TechieRagConfig"/> instance, and a settings screen binds its form fields directly onto
/// it. So the provider the operator merely SELECTED was written into the service's cache by the
/// two-way binding, before any save, and every later read reported it back as though it had been
/// persisted — including after a save that was refused for a missing base URL. The file, which is what
/// the RAG instance reads, was untouched.</para>
/// <para>These tests therefore assert three separate things: that the two sides name the same FILE
/// (not merely hold equal values), that a save round-trips through that file into what the manager
/// resolves, and that nothing which failed to reach the file can read back as though it had.</para>
/// </remarks>
public class LlmSettingsPersistenceTests
{
    /// <summary>The exact section set <c>LlmSettings.razor</c> passes to the save.</summary>
    private const TechieRagConfigSections LlmSettingsOwns =
        TechieRagConfigSections.Llm | TechieRagConfigSections.LlmFallback | TechieRagConfigSections.Prompt
        | TechieRagConfigSections.Resilience | TechieRagConfigSections.UsageTracking;

    /// <summary>
    /// ACCEPTANCE CLAUSE (3): the page's write path and the manager's read path name one file.
    /// </summary>
    /// <remarks>
    /// Asserts on the PATH, deliberately, not on values that happen to agree. Two independent stores
    /// holding equal contents are indistinguishable from one store until the moment they disagree,
    /// which is exactly how this defect stayed invisible. Both sides must derive the path from
    /// <see cref="DataDirectory.ConfigFilePath"/>, and the file the save actually produces must be the
    /// file at that path.
    /// </remarks>
    [Fact]
    public async Task TheSettingsWriteAndTheRagInstanceReadResolveTheSameConfigurationFile()
    {
        using var host = new ConfigEncryptionTestHost();
        var configService = host.CreateConfigService();
        var manager = host.CreateRagManager();

        var writePath = configService.ConfigFilePath;
        var readPath = manager.ResolveConfigFilePath();

        Assert.Equal(writePath, readPath);
        Assert.Equal(
            DataDirectory.ConfigFilePath(DataDirectory.Resolve(host.DataDirectoryPath)),
            writePath);

        // And the path is not merely agreed on — it is where the save genuinely lands.
        Assert.False(File.Exists(writePath));
        await configService.SaveConfigAsync(new TechieRagConfig(), TechieRagConfigSections.All);
        Assert.True(File.Exists(readPath));
    }

    /// <summary>
    /// ACCEPTANCE CLAUSES (1) and (2): saving Ollama the way the page saves it is what the RAG
    /// instance then resolves, with no restart and no hand-editing.
    /// </summary>
    /// <remarks>
    /// Drives the identical call <c>LlmSettings.SaveConfigAsync</c> makes — same section flags — and
    /// then asks the manager, through the same method its instance build uses, which provider it
    /// resolves. A separate manager instance is used so nothing can be served from memory.
    /// </remarks>
    [Fact]
    public async Task SavingAProviderFromLlmSettingsIsWhatTheRagInstanceResolves()
    {
        using var host = new ConfigEncryptionTestHost();

        var configService = host.CreateConfigService();
        var config = await configService.LoadConfigAsync();
        config.Llm.Source = LlmSource.Ollama;
        config.Llm.Endpoint = "http://localhost:11434";
        config.Llm.Model = "llama3.2";
        await configService.SaveConfigAsync(config, LlmSettingsOwns);

        var resolved = await host.CreateRagManager().ResolveConfiguredLlmAsync();

        Assert.NotNull(resolved);
        Assert.Equal(LlmSource.Ollama, resolved!.Source);
        Assert.Equal("http://localhost:11434", resolved.Endpoint);
        Assert.Equal("llama3.2", resolved.Model);
    }

    /// <summary>
    /// THE REGRESSION, clause (4): an edit that was never saved must not become the app's
    /// configuration.
    /// </summary>
    /// <remarks>
    /// Before the fix this failed. <c>LoadConfigAsync</c> returned the cached instance itself, so
    /// setting <c>Llm.Source</c> on the object a screen is bound to mutated the service's cache, and
    /// the very next read reported Ollama — with nothing on disk and nothing handed to the RAG
    /// instance. That is the whole "two different stores" symptom in four lines.
    /// </remarks>
    [Fact]
    public async Task AnUnsavedEditOnOneScreenNeverBecomesTheApplicationsConfiguration()
    {
        using var host = new ConfigEncryptionTestHost();
        var configService = host.CreateConfigService();

        var edited = await configService.LoadConfigAsync();
        edited.Llm.Source = LlmSource.Ollama;
        edited.Llm.Endpoint = "http://localhost:11434";
        edited.Llm.Model = "llama3.2";

        var readBack = await configService.LoadConfigAsync();

        Assert.Equal(LlmSource.None, readBack.Llm.Source);
        Assert.Null(await host.CreateRagManager().ResolveConfiguredLlmAsync());
    }

    /// <summary>
    /// THE REGRESSION, the operator's exact sequence: a refused save must not read back as applied.
    /// </summary>
    /// <remarks>
    /// <para>This is what the application log recorded on 2026-07-30 23:06:11 — the provider was
    /// selected, Save &amp; apply was refused because the required fields were empty, and the screen
    /// went on showing the provider anyway. Before the fix the assertion below saw
    /// <c>OpenAICompatible</c>, because the refusal left the service's cache already carrying the
    /// operator's selection.</para>
    /// <para>An OpenAI-compatible provider with no endpoint is used because that is the failure the
    /// service itself refuses (<c>BlocksInstanceBuild</c>); the page refuses a wider set on the same
    /// grounds.</para>
    /// </remarks>
    [Fact]
    public async Task ASaveTheServiceRefusesLeavesBothStoresUnchanged()
    {
        using var host = new ConfigEncryptionTestHost();
        var configService = host.CreateConfigService();
        await configService.SaveConfigAsync(new TechieRagConfig(), TechieRagConfigSections.All);

        var config = await configService.LoadConfigAsync();
        config.Llm.Source = LlmSource.OpenAICompatible;
        config.Llm.Model = "gpt-4o";

        await Assert.ThrowsAsync<LlmConfigValidationException>(
            () => configService.SaveConfigAsync(config, LlmSettingsOwns));

        var readBack = await configService.LoadConfigAsync();
        Assert.Equal(LlmSource.None, readBack.Llm.Source);
        Assert.Null(await host.CreateRagManager().ResolveConfiguredLlmAsync());
    }

    /// <summary>
    /// Verifies a saved provider survives a restart, read by a manager that shares no memory with
    /// the writer.
    /// </summary>
    /// <remarks>
    /// Every object is rebuilt over the same sandbox, which is what an application restart is. This is
    /// the honest version of the "it survives a restart" claim the defect report made about the
    /// in-memory cache.
    /// </remarks>
    [Fact]
    public async Task AProviderSavedFromLlmSettingsSurvivesAnApplicationRestart()
    {
        using var host = new ConfigEncryptionTestHost();

        var beforeRestart = host.CreateConfigService();
        var config = await beforeRestart.LoadConfigAsync();
        config.Llm.Source = LlmSource.Ollama;
        config.Llm.Endpoint = "http://localhost:11434";
        config.Llm.Model = "llama3.2";
        await beforeRestart.SaveConfigAsync(config, LlmSettingsOwns);

        var afterRestart = await host.CreateConfigService().LoadConfigAsync();
        Assert.Equal(LlmSource.Ollama, afterRestart.Llm.Source);
        Assert.Equal("llama3.2", afterRestart.Llm.Model);

        var resolved = await host.CreateRagManager().ResolveConfiguredLlmAsync();
        Assert.Equal(LlmSource.Ollama, resolved!.Source);
    }

    /// <summary>
    /// Verifies the base URL the form shows for a local server is a value that can be saved, not only
    /// a placeholder.
    /// </summary>
    /// <remarks>
    /// The page filled its Ollama base-URL box with <c>http://localhost:11434</c> as placeholder text
    /// only, so the bound field stayed empty and the save was refused with "Base URL is required for
    /// the Ollama provider". The form now writes this same value into the model when the provider is
    /// chosen; a remote provider's address is account-specific and must stay blank and required.
    /// </remarks>
    [Fact]
    public void EveryProviderWithAUsableDefaultEndpointSavesWithoutFurtherTyping()
    {
        Assert.True(LlmConfigValidator.HasUsableDefaultEndpoint(LlmSource.Ollama));
        Assert.True(LlmConfigValidator.HasUsableDefaultEndpoint(LlmSource.LmStudio));
        Assert.False(LlmConfigValidator.HasUsableDefaultEndpoint(LlmSource.OpenAICompatible));
        Assert.False(LlmConfigValidator.HasUsableDefaultEndpoint(LlmSource.AzureAIFoundry));

        foreach (var source in new[] { LlmSource.Ollama, LlmSource.LmStudio })
        {
            var candidate = new LlmConfig
            {
                Source = source,
                Endpoint = LlmConfigValidator.DefaultEndpoint(source),
                Model = "llama3.2"
            };

            Assert.NotEmpty(candidate.Endpoint!);
            Assert.Empty(LlmConfigValidator.Validate(candidate));
        }
    }
}
