using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using TechieRag.Agents.DependencyInjection;
using TechieRag.Agents.Tests.TestDoubles;
using TechieRag.Embedding;
using Xunit;

namespace TechieRag.Agents.Tests;

/// <summary>Registration and dependency-compatibility tests for the Agents package (REQ-RAG-016 / BRD-84).</summary>
public class ServiceRegistrationTests
{
    /// <summary>AddTechieRagAgent registers the agent and a keyed Agent Framework agent, built from the container's TechieRag.</summary>
    [Fact]
    public void AddTechieRagAgentRegistersBoth()
    {
        var model = new ScriptedChatClient();
        var services = new ServiceCollection()
            .AddSingleton<ITechieRag>(AgentTestRag.Create())
            .AddTechieRagAgent(builder => builder.UseCustomChatClient(() => model));
        using var provider = services.BuildServiceProvider();

        var agent = provider.GetRequiredService<ITechieRagAgent>();
        var keyed = provider.GetRequiredKeyedService<AIAgent>(TechieRagAgentServiceCollectionExtensions.AgentServiceKey);

        Assert.Same(agent.Agent, keyed);
    }

    /// <summary>
    /// The OpenAI SDK unifies to the version Microsoft.Extensions.AI.OpenAI needs (2.13.x) in any app that
    /// references both packages, and core's Azure OpenAI embedding provider (built on Azure.AI.OpenAI 2.1.0)
    /// still constructs against it.
    /// </summary>
    [Fact]
    public void CoreAzureProviderLoadsWithUnifiedOpenAI()
    {
        using var embedding = new AzureOpenAIEmbeddingProvider("https://example.openai.azure.com", "test-key", "text-embedding-3-small");

        Assert.Equal("Azure OpenAI", embedding.Name);
        Assert.True(typeof(OpenAI.OpenAIClient).Assembly.GetName().Version >= new Version(2, 13));
    }
}
