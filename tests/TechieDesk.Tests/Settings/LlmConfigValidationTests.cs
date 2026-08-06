using TechieDesk.Services;
using TechieRag;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Settings;

/// <summary>
/// REQ-UI-043 / BRD-136: <c>/llm-settings</c> shows only the fields the chosen provider needs, marks
/// each of them required, and refuses to save while any of them is empty — with the failure named on
/// the offending field.
/// </summary>
/// <remarks>
/// <para>The defect class these tests close: an OpenAI-compatible provider saved with an API key and
/// no endpoint used to persist cleanly and then throw
/// <c>InvalidOperationException: Endpoint is required for OpenAI-compatible LLM provider</c> out of
/// every unrelated page that builds a TechieRag instance, <c>/token-usage</c> included.</para>
/// </remarks>
public sealed class LlmConfigValidationTests
{
    /// <summary>
    /// The regression guard. Saving an OpenAI-compatible provider that has an API key but no endpoint
    /// must be refused by the configuration service, and nothing may reach disk.
    /// </summary>
    [Fact]
    public async Task SaveRejectsOpenAiCompatibleWithoutEndpoint()
    {
        using var host = new ConfigEncryptionTestHost();
        var config = new TechieRagConfig
        {
            Llm = new LlmConfig
            {
                Source = LlmSource.OpenAICompatible,
                ApiKey = "sk-only-a-key",
                Model = "gpt-4o"
            }
        };

        var thrown = await Assert.ThrowsAsync<LlmConfigValidationException>(
            () => host.CreateConfigService().SaveConfigAsync(config));

        Assert.Contains(thrown.Errors, e => e.Field == LlmConfigValidator.EndpointField);

        // REQ-UI-051: the exception text is the INVARIANT log form — the resource key plus the
        // provider — because it lands in the application log and in a debugger, never on a screen.
        Assert.Contains("LlmValidationBaseUrlRequired", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("OpenAI-compatible", thrown.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(host.ConfigFilePath));
    }

    /// <summary>
    /// The same half-configured provider is refused when it sits on the fallback slot rather than the
    /// primary one, because the builder constructs both.
    /// </summary>
    [Fact]
    public async Task SaveRejectsUnbuildableFallbackProvider()
    {
        using var host = new ConfigEncryptionTestHost();
        var config = new TechieRagConfig
        {
            Llm = new LlmConfig { Source = LlmSource.Ollama, Endpoint = "http://localhost:11434", Model = "llama3.2" },
            LlmFallback = new LlmConfig { Source = LlmSource.Anthropic, Model = "claude-sonnet-4-5-20250929" }
        };

        var thrown = await Assert.ThrowsAsync<LlmConfigValidationException>(
            () => host.CreateConfigService().SaveConfigAsync(config));

        Assert.Contains(thrown.Errors, e => e.Field == LlmConfigValidator.ApiKeyField);
    }

    /// <summary>
    /// A fully configured provider still saves and reloads, so the guard blocks only the broken case.
    /// </summary>
    [Fact]
    public async Task SaveAcceptsAFullyConfiguredProvider()
    {
        using var host = new ConfigEncryptionTestHost();
        var config = new TechieRagConfig
        {
            Llm = new LlmConfig
            {
                Source = LlmSource.OpenAICompatible,
                Endpoint = "https://api.openai.com",
                ApiKey = "sk-complete",
                Model = "gpt-4o"
            }
        };

        await host.CreateConfigService().SaveConfigAsync(config);
        var reloaded = await host.CreateConfigService().LoadConfigAsync();

        Assert.Equal(LlmSource.OpenAICompatible, reloaded.Llm.Source);
        Assert.Equal("https://api.openai.com", reloaded.Llm.Endpoint);
    }

    /// <summary>
    /// Choosing no provider at all is always valid — there is nothing to misconfigure, and the app
    /// stays usable in retrieval-only mode.
    /// </summary>
    [Fact]
    public async Task SaveAcceptsTheNoneProvider()
    {
        using var host = new ConfigEncryptionTestHost();

        await host.CreateConfigService().SaveConfigAsync(new TechieRagConfig());

        Assert.True(File.Exists(host.ConfigFilePath));
        Assert.Empty(LlmConfigValidator.Validate(new LlmConfig { Source = LlmSource.None }));
    }

    /// <summary>
    /// Ollama and LM Studio need a base URL and a model and nothing else — the API key field is not
    /// part of their field set, so its absence is never an error.
    /// </summary>
    /// <param name="source">The local provider under test.</param>
    [Theory]
    [InlineData(LlmSource.Ollama)]
    [InlineData(LlmSource.LmStudio)]
    public void LocalProvidersNeedABaseUrlAndNoApiKey(LlmSource source)
    {
        Assert.True(LlmConfigValidator.RequiresEndpoint(source));
        Assert.False(LlmConfigValidator.RequiresApiKey(source));
        Assert.False(LlmConfigValidator.UsesDeploymentName(source));
        Assert.False(LlmConfigValidator.RequiresApiVersion(source));

        var complete = new LlmConfig { Source = source, Endpoint = "http://localhost:11434", Model = "llama3.2" };
        Assert.Empty(LlmConfigValidator.Validate(complete));

        var missingEndpoint = new LlmConfig { Source = source, Model = "llama3.2" };
        var errors = LlmConfigValidator.Validate(missingEndpoint);
        Assert.Single(errors);
        Assert.Equal(LlmConfigValidator.EndpointField, errors[0].Field);
    }

    /// <summary>
    /// Azure AI Foundry needs an endpoint, a deployment name and an API version, and it has no model
    /// box because Azure addresses deployments rather than model names.
    /// </summary>
    [Fact]
    public void AzureNeedsEndpointDeploymentAndApiVersion()
    {
        Assert.True(LlmConfigValidator.RequiresEndpoint(LlmSource.AzureAIFoundry));
        Assert.True(LlmConfigValidator.RequiresApiVersion(LlmSource.AzureAIFoundry));
        Assert.True(LlmConfigValidator.UsesDeploymentName(LlmSource.AzureAIFoundry));

        var bare = new LlmConfig { Source = LlmSource.AzureAIFoundry };
        var fields = LlmConfigValidator.Validate(bare).Select(e => e.Field).ToList();

        Assert.Contains(LlmConfigValidator.EndpointField, fields);
        Assert.Contains(LlmConfigValidator.ModelField, fields);
        Assert.Contains(LlmConfigValidator.ApiVersionField, fields);
        Assert.Contains(LlmConfigValidator.ApiKeyField, fields);

        var complete = new LlmConfig
        {
            Source = LlmSource.AzureAIFoundry,
            Endpoint = "https://my-resource.openai.azure.com",
            Model = "gpt-4o-prod",
            ApiVersion = "2024-10-21",
            ApiKey = "azure-key"
        };
        Assert.Empty(LlmConfigValidator.Validate(complete));
    }

    /// <summary>
    /// The deployment-name failure is worded against the deployment field, not against a "model" the
    /// Azure form never shows.
    /// </summary>
    [Fact]
    public void AzureNamesTheDeploymentFieldInItsError()
    {
        var errors = LlmConfigValidator.Validate(new LlmConfig
        {
            Source = LlmSource.AzureAIFoundry,
            Endpoint = "https://my-resource.openai.azure.com",
            ApiVersion = "2024-10-21",
            ApiKey = "azure-key"
        });

        using var resources = new ResourceHarness("en");
        var modelError = Assert.Single(errors);

        Assert.Equal(LlmConfigValidator.ModelField, modelError.Field);
        Assert.Equal("LlmValidationDeploymentRequired", modelError.MessageKey);
        Assert.Contains(
            "Deployment name is required",
            resources.Require(modelError.MessageKey, modelError.ProviderName),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-UI-051: every message the validator can raise resolves through the resources in both
    /// languages, keeps its provider placeholder, and names the provider in Latin script.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// The validator's messages reach the LLM Settings form, <c>TechieRagManager</c>'s exception
    /// text and the log. Only the first of those is read by a user, so the error carries a KEY plus
    /// the provider name rather than a sentence — and the page no longer has to re-derive which of
    /// six messages was raised from the field constant alone.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryValidationMessageResolvesThroughTheResources(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var sources = Enum.GetValues<LlmSource>().Where(source => source != LlmSource.None);
        var seen = new List<string>();

        foreach (var source in sources)
        {
            foreach (var error in LlmConfigValidator.Validate(new LlmConfig { Source = source }))
            {
                seen.Add(error.MessageKey);

                Assert.DoesNotContain(' ', error.MessageKey);

                var rendered = resources.Require(error.MessageKey, error.ProviderName);
                Assert.DoesNotContain("{0}", rendered, StringComparison.Ordinal);

                // The provider is a brand and stays in Latin script inside the translated sentence.
                Assert.Equal(LlmConfigValidator.DescribeSource(source), error.ProviderName);
                Assert.Contains(error.ProviderName, rendered, StringComparison.Ordinal);
            }
        }

        // All six message variants are exercised, so none of them is untested by accident.
        Assert.Equal(6, seen.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The hosted key-only providers need an API key and a model, and have no base URL field at all.
    /// </summary>
    /// <param name="source">The hosted provider under test.</param>
    [Theory]
    [InlineData(LlmSource.GoogleGemini)]
    [InlineData(LlmSource.Anthropic)]
    public void HostedProvidersNeedAKeyAndNoBaseUrl(LlmSource source)
    {
        Assert.False(LlmConfigValidator.RequiresEndpoint(source));
        Assert.True(LlmConfigValidator.RequiresApiKey(source));

        var complete = new LlmConfig { Source = source, ApiKey = "hosted-key", Model = "some-model" };
        Assert.Empty(LlmConfigValidator.Validate(complete));

        var missingKey = new LlmConfig { Source = source, Model = "some-model" };
        var keyError = Assert.Single(LlmConfigValidator.Validate(missingKey));
        Assert.Equal(LlmConfigValidator.ApiKeyField, keyError.Field);
        Assert.True(keyError.BlocksInstanceBuild);
    }

    /// <summary>
    /// Every provider except None requires a model or deployment name, and the failure is named on
    /// the model field so the form can highlight it.
    /// </summary>
    /// <param name="source">The provider under test.</param>
    [Theory]
    [InlineData(LlmSource.Ollama)]
    [InlineData(LlmSource.LmStudio)]
    [InlineData(LlmSource.OpenAICompatible)]
    [InlineData(LlmSource.GoogleGemini)]
    [InlineData(LlmSource.Anthropic)]
    public void EveryConfiguredProviderRequiresAModel(LlmSource source)
    {
        var config = new LlmConfig
        {
            Source = source,
            Endpoint = LlmConfigValidator.RequiresEndpoint(source) ? "http://localhost:1234" : null,
            ApiKey = LlmConfigValidator.RequiresApiKey(source) ? "a-key" : null
        };

        var error = Assert.Single(LlmConfigValidator.Validate(config));
        Assert.Equal(LlmConfigValidator.ModelField, error.Field);
    }

    /// <summary>
    /// Only the failures that make <c>TechieRagBuilder.Build()</c> throw are build blocking, so a
    /// merely incomplete configuration never takes the whole app down.
    /// </summary>
    [Fact]
    public void OnlyBuilderThrowingFailuresBlockTheInstanceBuild()
    {
        var noEndpoint = new LlmConfig { Source = LlmSource.OpenAICompatible, ApiKey = "k", Model = "m" };
        Assert.False(LlmConfigValidator.IsBuildable(noEndpoint));

        var noModel = new LlmConfig { Source = LlmSource.OpenAICompatible, Endpoint = "https://api.openai.com", ApiKey = "k" };
        Assert.True(LlmConfigValidator.IsBuildable(noModel));
        Assert.NotEmpty(LlmConfigValidator.Validate(noModel));
    }

    /// <summary>
    /// The whole-configuration pass reports failures from the primary and the fallback provider, and
    /// can narrow itself to only the build-blocking ones.
    /// </summary>
    [Fact]
    public void WholeConfigValidationCoversBothProviders()
    {
        var config = new TechieRagConfig
        {
            Llm = new LlmConfig { Source = LlmSource.OpenAICompatible, ApiKey = "k" },
            LlmFallback = new LlmConfig { Source = LlmSource.Anthropic }
        };

        var all = LlmConfigValidator.ValidateConfig(config, buildBlockingOnly: false);
        var blocking = LlmConfigValidator.ValidateConfig(config, buildBlockingOnly: true);

        Assert.True(all.Count > blocking.Count);
        Assert.All(blocking, e => Assert.True(e.BlocksInstanceBuild));
        Assert.Equal(2, blocking.Count);
    }
}
