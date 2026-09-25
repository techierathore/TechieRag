using TechieRag.Llm;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// <c>UseLocalLlm()</c>, <c>LlmSource.Local</c>, the factory arm and the <c>local/&lt;model&gt;</c>
/// route (REQ-RAG-057 / BRD-96, REQ-RAG-064 / BRD-104). Nothing here downloads or loads a model.
/// </summary>
public sealed class LocalRegistrationTests
{
    /// <summary>
    /// With LlmSource.Local registered, the factory builds the local provider from a
    /// <c>local/&lt;model&gt;</c> name with no endpoint and no key.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-064 FactoryBuildsLocalProviderWithoutKey")]
    public void FactoryBuildsLocalProviderWithoutKey()
    {
        LocalLlm.Register();

        using var provider = (LocalLlmProvider)LlmProviderFactory.CreateForModel("local/qwen2.5-0.5b-instruct", apiKey: null);

        Assert.Equal("qwen2.5-0.5b-instruct", provider.ModelName);
        Assert.False(provider.IsLoaded);
    }

    /// <summary>ModelRouter resolves <c>local/&lt;model&gt;</c> to the local connector, which has no endpoint and needs no key.</summary>
    [Fact(DisplayName = "REQ-RAG-064 RouterResolvesLocalModel")]
    public void RouterResolvesLocalModel()
    {
        var route = ModelRouter.Require("local/phi-3-mini-4k-instruct");

        Assert.Equal(LlmSource.Local, route.Connector.Source);
        Assert.Equal("phi-3-mini-4k-instruct", route.ModelId);
        Assert.Null(route.Connector.Endpoint);
        Assert.False(route.Connector.RequiresApiKey);
    }

    /// <summary>UseLocalLlm(id) records LlmSource.Local and the model, with no endpoint and no key, through UseCustomLlmProvider.</summary>
    [Fact]
    public void UseLocalLlmConfiguresLocalSource()
    {
        var builder = new TechieRagBuilder().UseLocalLlm("qwen2.5-0.5b-instruct", o => o.TermsAccepted = true);

        var llm = builder.GetConfig().Llm;

        Assert.Equal((LlmSource.Local, "qwen2.5-0.5b-instruct"), (llm.Source, llm.Model));
        Assert.Null(llm.Endpoint);
        Assert.Null(llm.ApiKey);
        Assert.True(LocalLlmProviderRegistry.IsRegistered);
    }

    /// <summary>UseLocalLlm() with no arguments picks the platform default model.</summary>
    [Fact]
    public void UseLocalLlmDefaultsToPlatformModel()
    {
        var builder = new TechieRagBuilder().UseLocalLlm();

        Assert.Equal(LocalModel.PlatformDefault.Id, builder.GetConfig().Llm.Model);
    }

    /// <summary>The folder overload uses the host's folder and its name as the model id; nothing downloads.</summary>
    [Fact]
    public void UseLocalLlmFolderUsesFolderName()
    {
        var folder = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "my-own-model"));

        var builder = new TechieRagBuilder().UseLocalLlm(folder, LocalChatTemplate.Llama3, contextLength: 8_192);

        Assert.Equal("my-own-model", builder.GetConfig().Llm.Model);
    }

    /// <summary>An unknown model id is refused with the known ids listed.</summary>
    [Fact]
    public void UnknownModelIdListsKnownModels()
    {
        var error = Assert.Throws<ArgumentException>(() => new TechieRagBuilder().UseLocalLlm("no-such-model"));

        Assert.Contains("qwen2.5-0.5b-instruct", error.Message);
        Assert.Contains("phi-3-mini-4k-instruct", error.Message);
    }
}
