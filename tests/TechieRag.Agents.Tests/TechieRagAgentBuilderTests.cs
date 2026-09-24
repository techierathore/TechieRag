using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TechieRag.Agentic;
using TechieRag.Agents.Tests.TestDoubles;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Agents.Tests;

/// <summary>
/// Tests for <see cref="TechieRagAgentBuilder"/> and the agent it builds (REQ-RAG-016 / BRD-84).
/// </summary>
/// <remarks>
/// The model is a scripted <see cref="IChatClient"/>; everything between it and the documents is real:
/// Agent Framework's <c>ChatClientAgent</c> and function-invoking loop, the retrieval context provider,
/// the core knowledge-base tools, and a real <see cref="TechieRagClient"/> over an in-memory store.
/// </remarks>
public class TechieRagAgentBuilderTests
{
    /// <summary>
    /// Acceptance: an agent built with <see cref="TechieRagAgentBuilder"/> over a TechieRag instance
    /// answers from the documents through Microsoft Agent Framework — the model's search call runs
    /// against TechieRag, the model reads the passage with its ref, and the answer carries the typed source.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-016 BuiltAgentAnswersFromDocuments")]
    public async Task BuiltAgentAnswersFromDocuments()
    {
        var model = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("c1", "search_knowledge_base", new() { ["query"] = "refund window days" }),
            ScriptedChatClient.Answer("You have 30 days to return it [S1]."));
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create()).UseCustomChatClient(() => model).Build();

        var response = await agent.AskAsync("How long do I have to return something?");

        Assert.Equal("You have 30 days to return it [S1].", response.Answer);
        Assert.Equal(AgentTestRag.RefundPassage, Assert.Single(response.Sources).Chunk.Text);
        Assert.Equal(KnowledgeBaseTools.StatusStrong, Assert.Single(response.Searches).Status);
        var toolResult = model.Calls[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        using var json = JsonDocument.Parse((string)toolResult.Result!);
        Assert.Equal("S1", json.RootElement.GetProperty("results")[0].GetProperty("ref").GetString());
        Assert.IsType<ChatClientAgent>(agent.Agent);
    }

    /// <summary>The model is offered the two knowledge-base tools and the default retrieve-first instructions.</summary>
    [Fact]
    public async Task BuiltAgentOffersToolsAndInstructions()
    {
        var model = new ScriptedChatClient(ScriptedChatClient.Answer("Hello."));
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create()).UseCustomChatClient(() => model).Build();

        await agent.AskAsync("hi");

        var options = model.Options[0]!;
        Assert.Equal(["search_knowledge_base", "list_documents"], options.Tools!.Select(t => t.Name).Order().Reverse());
        Assert.Equal(AgenticInstructions.Default, options.Instructions);
    }

    /// <summary>Additional instructions become domain guidance after the unchanged default rules.</summary>
    [Fact]
    public async Task AdditionalInstructionsBecomeDomainGuidance()
    {
        var model = new ScriptedChatClient(ScriptedChatClient.Answer("Hello."));
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create())
            .UseCustomChatClient(() => model)
            .WithAdditionalInstructions("You are the returns desk.")
            .Build();

        await agent.AskAsync("hi");

        Assert.Equal(AgenticInstructions.WithDomainGuidance("You are the returns desk."), model.Options[0]!.Instructions);
    }

    /// <summary>
    /// A streamed turn yields the sources after the search, then the answer tokens, then one completed
    /// event with the whole answer.
    /// </summary>
    [Fact]
    public async Task StreamedTurnYieldsSourcesTokensCompleted()
    {
        var model = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("c1", "search_knowledge_base", new() { ["query"] = "refund window" }),
            ScriptedChatClient.Answer("Thirty days [S1]."));
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create()).UseCustomChatClient(() => model).Build();

        var events = new List<RagStreamEvent>();
        await foreach (var streamEvent in agent.AskStreamAsync("Return window?")) events.Add(streamEvent);

        var sourcesAt = events.FindIndex(e => e.Type == RagStreamEventType.Sources);
        var firstToken = events.FindIndex(e => e.Type == RagStreamEventType.Token);
        Assert.True(sourcesAt >= 0 && sourcesAt < firstToken);
        Assert.Single(events[sourcesAt].Sources!);
        Assert.Equal("Thirty days [S1].", events[^1].Answer);
    }

    /// <summary>A session keeps refs across turns: the same passage found again keeps S1.</summary>
    [Fact]
    public async Task SessionKeepsRefsAcrossTurns()
    {
        var model = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("c1", "search_knowledge_base", new() { ["query"] = "refund" }),
            ScriptedChatClient.Answer("30 days [S1]."),
            ScriptedChatClient.ToolCall("c2", "search_knowledge_base", new() { ["query"] = "receipt" }),
            ScriptedChatClient.Answer("Yes, a receipt [S1]."));
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create()).UseCustomChatClient(() => model).Build();
        var session = await agent.CreateSessionAsync();

        await agent.AskAsync("Return window?", session);
        var second = await agent.AskAsync("Do I need a receipt?", session);

        Assert.Equal(["S1"], Assert.Single(second.Searches).Refs);
        Assert.Equal(1, Assert.Single(second.Searches).Refs.Count);
    }

    /// <summary>The tool-iteration cap bounds a model that never stops calling tools.</summary>
    [Fact]
    public async Task MaxToolIterationsBoundsTheLoop()
    {
        var model = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("c1", "list_documents", new()),
            ScriptedChatClient.ToolCall("c2", "list_documents", new()),
            ScriptedChatClient.ToolCall("c3", "list_documents", new()),
            ScriptedChatClient.Answer("Stopped."));
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create()).UseCustomChatClient(() => model).WithMaxToolIterations(2).Build();

        await agent.AskAsync("loop");

        Assert.True(model.Calls.Count <= 3, $"Expected at most 3 model calls, saw {model.Calls.Count}.");
    }

    /// <summary>Building without choosing a model fails with a message naming the choices.</summary>
    [Fact]
    public void BuildWithoutModelThrows()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new TechieRagAgentBuilder(AgentTestRag.Create()).Build());

        Assert.Contains("UseLmStudio", ex.Message);
    }

    /// <summary>UseConfiguredLlm on an instance with no LLM fails at build, not at the first question.</summary>
    [Fact]
    public void ConfiguredLlmWithoutLlmThrows()
    {
        var builder = new TechieRagAgentBuilder(AgentTestRag.Create()).UseConfiguredLlm();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    /// <summary>
    /// The LM Studio route builds an OpenAI Chat Completions client at <c>{endpoint}/v1</c> without any
    /// network call, and the agent carries the configured name.
    /// </summary>
    [Fact]
    public void LmStudioRouteBuildsWithoutNetwork()
    {
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create()).UseLmStudio("http://localhost:1234", "qwen3-8b").WithName("returns").Build();

        Assert.Equal("returns", agent.Agent.Name);
        Assert.NotNull(agent.Agent.GetService<IChatClient>());
    }
}
