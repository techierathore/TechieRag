using TechieRag.Llm;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Unit tests for model-name to provider routing and the named connector catalog (REQ-RAG-034).
/// </summary>
public class ModelRouterTests
{
    /// <summary>A Claude model name routes to Anthropic's native provider.</summary>
    [Fact]
    public void ClaudeModelRoutesToAnthropic()
    {
        var route = ModelRouter.Require("claude-sonnet-4-5-20250929");

        Assert.Equal("anthropic", route.Connector.Name);
        Assert.Equal(LlmSource.Anthropic, route.Source);
        Assert.Equal("claude-sonnet-4-5-20250929", route.ModelId);
    }

    /// <summary>A GPT model name routes to OpenAI.</summary>
    [Fact(DisplayName = "REQ-RAG-099 GptModelRoutesToOpenAi")]
    public void GptModelRoutesToOpenAi()
    {
        Assert.Equal("openai", ModelRouter.Require("gpt-4o-mini").Connector.Name);
    }

    /// <summary>
    /// REQ-RAG-099 / BRD-146: <c>UseLlmForModel("gpt-4o")</c> alone — no provider, endpoint or connector
    /// named — builds an instance whose LLM is the OpenAI-compatible provider for gpt-4o, pointed at
    /// the OpenAI connector's endpoint.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-099 BuilderResolvesOpenAiFromTheModelNameAlone")]
    public void BuilderResolvesOpenAiFromTheModelNameAlone()
    {
        var rag = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new TestDoubles.FakeEmbeddingProvider())
            .UseSqliteVec(Path.Combine(Path.GetTempPath(), $"trroute-{Guid.NewGuid():N}.db"))
            .UseLlmForModel("gpt-4o", "test-key")
            .Build();

        var llm = rag.GetLlmProvider();

        Assert.NotNull(llm);
        Assert.Equal("gpt-4o", llm!.ModelName);
        var provider = Assert.IsType<OpenAICompatibleLlmProvider>(FieldOfType<TechieRag.Abstractions.ILlmProvider>(llm));
        var http = FieldOfType<HttpClient>(provider);
        Assert.Equal("api.openai.com", http.BaseAddress!.Host);
    }

    private static T FieldOfType<T>(object owner) where T : class =>
        owner.GetType()
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.GetValue(owner))
            .OfType<T>()
            .First();

    /// <summary>A Gemini model name routes to Google's native provider.</summary>
    [Fact]
    public void GeminiModelRoutesToGoogle()
    {
        var route = ModelRouter.Require("gemini-2.0-flash");

        Assert.Equal(LlmSource.GoogleGemini, route.Source);
    }

    /// <summary>Longest matching prefix wins, so a compound Mistral name is not mis-assigned.</summary>
    [Fact]
    public void LongestPrefixWins()
    {
        Assert.Equal("mistral", ModelRouter.Require("open-mistral-nemo").Connector.Name);
    }

    /// <summary>Routing is case-insensitive.</summary>
    [Fact]
    public void RoutingIsCaseInsensitive()
    {
        Assert.Equal("deepseek", ModelRouter.Require("DeepSeek-Chat").Connector.Name);
    }

    /// <summary>An open-weight name served by many providers resolves to none rather than guessing.</summary>
    [Fact]
    public void AmbiguousOpenWeightModelDoesNotResolve()
    {
        Assert.Null(ModelRouter.Resolve("llama-3.3-70b-versatile"));
    }

    /// <summary>Requiring an ambiguous model explains how to qualify it.</summary>
    [Fact]
    public void RequireExplainsHowToQualifyAnAmbiguousModel()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModelRouter.Require("llama-3.3-70b-versatile"));

        Assert.Contains("connector/model", error.Message);
    }

    /// <summary>An explicit connector prefix routes the model and is stripped from the model id.</summary>
    [Fact]
    public void ExplicitConnectorPrefixRoutesAndIsStripped()
    {
        var route = ModelRouter.Require("groq/llama-3.3-70b-versatile");

        Assert.Equal("groq", route.Connector.Name);
        Assert.Equal("llama-3.3-70b-versatile", route.ModelId);
        Assert.Equal("https://api.groq.com/openai/v1", route.Endpoint);
    }

    /// <summary>Only the first slash is addressing, so an OpenRouter vendor path survives intact.</summary>
    [Fact]
    public void OnlyTheFirstSlashIsTreatedAsAddressing()
    {
        var route = ModelRouter.Require("openrouter/anthropic/claude-sonnet-4-5");

        Assert.Equal("openrouter", route.Connector.Name);
        Assert.Equal("anthropic/claude-sonnet-4-5", route.ModelId);
    }

    /// <summary>An explicit prefix overrides what the bare name would have matched.</summary>
    [Fact]
    public void ExplicitConnectorOverridesPrefixMatching()
    {
        var route = ModelRouter.Require("openrouter/gpt-4o");

        Assert.Equal("openrouter", route.Connector.Name);
    }

    /// <summary>A blank model name resolves to nothing.</summary>
    [Fact]
    public void BlankModelNameDoesNotResolve()
    {
        Assert.Null(ModelRouter.Resolve("  "));
    }

    /// <summary>An unknown connector name is reported with the list of known ones.</summary>
    [Fact]
    public void UnknownConnectorIsReportedWithKnownNames()
    {
        var error = Assert.Throws<InvalidOperationException>(() => LlmConnectorCatalog.Require("nope"));

        Assert.Contains("groq", error.Message);
    }

    /// <summary>Local runtimes are in the catalog and need no API key.</summary>
    [Fact]
    public void LocalRuntimesRequireNoApiKey()
    {
        Assert.False(LlmConnectorCatalog.Require("ollama").RequiresApiKey);
        Assert.False(LlmConnectorCatalog.Require("lmstudio").RequiresApiKey);
    }

    /// <summary>A connector requiring a key refuses to build a provider without one.</summary>
    [Fact]
    public void ConnectorRequiringAKeyRefusesToBuildWithoutOne()
    {
        var route = ModelRouter.Require("groq/llama-3.3-70b-versatile");

        Assert.Throws<InvalidOperationException>(() => LlmProviderFactory.Create(route, null));
    }

    /// <summary>The factory builds the native provider for a natively-supported connector.</summary>
    [Fact]
    public void FactoryBuildsNativeProviderForAnthropic()
    {
        var provider = LlmProviderFactory.CreateForModel("claude-sonnet-4-5-20250929", "test-key");

        Assert.Equal("claude-sonnet-4-5-20250929", provider.ModelName);
        Assert.True(provider.SupportsToolCalling);
    }
}
