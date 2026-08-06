using TechieRag.Llm;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Unit tests for how <see cref="OpenAICompatibleLlmProvider"/> composes its request URI
/// (REQ-RAG-034). Several catalog connectors — Groq, OpenRouter, Fireworks, Cohere — publish an
/// endpoint with a base path, and an absolute request path would silently discard it.
/// </summary>
public class OpenAiEndpointCompositionTests
{
    /// <summary>An endpoint with a base path keeps that path in the request URI.</summary>
    [Theory]
    [InlineData("https://api.groq.com/openai/v1", "https://api.groq.com/openai/v1/chat/completions")]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("https://api.fireworks.ai/inference/v1", "https://api.fireworks.ai/inference/v1/chat/completions")]
    [InlineData("https://api.cohere.ai/compatibility/v1", "https://api.cohere.ai/compatibility/v1/chat/completions")]
    public void BasePathEndpointsKeepTheirPath(string endpoint, string expected)
    {
        var provider = new OpenAICompatibleLlmProvider(endpoint, "key", "model");

        Assert.Equal(expected, provider.EffectiveCompletionsUri.ToString());
    }

    /// <summary>Endpoints without a base path resolve exactly as they always did.</summary>
    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.perplexity.ai", "https://api.perplexity.ai/chat/completions")]
    [InlineData("https://api.mistral.ai/v1/", "https://api.mistral.ai/v1/chat/completions")]
    public void PlainEndpointsResolveUnchanged(string endpoint, string expected)
    {
        var provider = new OpenAICompatibleLlmProvider(endpoint, "key", "model");

        Assert.Equal(expected, provider.EffectiveCompletionsUri.ToString());
    }

    /// <summary>Every OpenAI-compatible connector in the catalog composes a usable completions URI.</summary>
    [Fact]
    public void EveryCatalogEndpointComposesACompletionsUri()
    {
        foreach (var connector in LlmConnectorCatalog.All.Where(c => c.Source == LlmSource.OpenAICompatible))
        {
            var provider = new OpenAICompatibleLlmProvider(connector.Endpoint!, "key", "model");

            Assert.EndsWith("/chat/completions", provider.EffectiveCompletionsUri.ToString());
            Assert.StartsWith(connector.Endpoint!.TrimEnd('/'), provider.EffectiveCompletionsUri.ToString());
        }
    }
}
