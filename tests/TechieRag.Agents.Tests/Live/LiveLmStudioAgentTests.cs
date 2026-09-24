using TechieRag.Agents.Tests.TestDoubles;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Agents.Tests.Live;

/// <summary>
/// Live smoke tests of <see cref="TechieRagAgentBuilder.UseLmStudio"/> against a real LM Studio server
/// (REQ-RAG-016 / BRD-84).
/// </summary>
[Trait("Category", LiveLmStudioFactAttribute.CategoryName)]
public class LiveLmStudioAgentTests
{
    /// <summary>
    /// A real model answers a document question through Agent Framework and actually calls
    /// search_knowledge_base (a real tool call, not tool-call text — LM Studio bug tracker #2115), and the
    /// answer carries the retrieved passage as a typed source.
    /// </summary>
    [LiveLmStudioFact(DisplayName = "REQ-RAG-016 LmStudioAgentSearchesAndAnswers")]
    public async Task LmStudioAgentSearchesAndAnswers()
    {
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create())
            .UseLmStudio(LiveLmStudioFactAttribute.Endpoint, LiveLmStudioFactAttribute.Model!)
            .Build();

        var response = await agent.AskAsync("How many days do I have to return a purchase?");

        Assert.NotEmpty(response.Searches);
        Assert.Contains(response.Sources, s => s.Chunk.Text == AgentTestRag.RefundPassage);
        Assert.Contains("30", response.Answer);
    }

    /// <summary>The same agent over TechieRag's own LM Studio provider (adapter 1) streams tokens and searches.</summary>
    [LiveLmStudioFact]
    public async Task ConfiguredLmStudioProviderStreams()
    {
        var provider = new Llm.LmStudioLlmProvider(LiveLmStudioFactAttribute.Endpoint, LiveLmStudioFactAttribute.Model!);
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create(provider)).UseConfiguredLlm().Build();

        var events = new List<RagStreamEvent>();
        await foreach (var streamEvent in agent.AskStreamAsync("How many days do I have to return a purchase?")) events.Add(streamEvent);

        Assert.Contains(events, e => e.Type == RagStreamEventType.Sources);
        Assert.Contains(events, e => e.Type == RagStreamEventType.Token);
        Assert.Contains("30", events[^1].Answer);
    }
}
