using Microsoft.Extensions.AI;
using TechieRag.Agents.Interop;
using TechieRag.Agents.Tests.TestDoubles;
using TechieRag.Models;
using Xunit;
using static TechieRag.Agents.Tests.TestDoubles.ScriptedLlmProvider;
using MeaiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace TechieRag.Agents.Tests.Interop;

/// <summary>
/// Tests for adapter 1, <see cref="LlmProviderChatClient"/> (REQ-RAG-017 / BRD-85), including its
/// streaming over typed events (REQ-RAG-068 / BRD-111).
/// </summary>
public class LlmProviderChatClientTests
{
    private static readonly AIFunction Weather = AIFunctionFactory.Create((string city) => $"22 C in {city}", "get_weather", "Gets the weather.");

    /// <summary>MEAI tools reach the provider as tool definitions with the raw schema, and its tool call comes back as a function call.</summary>
    [Fact]
    public async Task ResponseMapsToolsAndToolCalls()
    {
        var provider = new ScriptedLlmProvider([Call("c1", "get_weather", """{"city":"Paris"}"""), Done("tool_calls")]);
        var client = new LlmProviderChatClient(provider);

        var response = await client.GetResponseAsync([new MeaiChatMessage(ChatRole.User, "Weather?")], new ChatOptions { Tools = [Weather], Instructions = "Be brief." });

        var definition = Assert.Single(provider.Options[0]!.Tools!);
        Assert.Equal("get_weather", definition.Name);
        Assert.Contains("city", definition.ParametersSchema);
        Assert.Equal("system", provider.Calls[0][0].Role);
        var call = Assert.Single(response.Messages[0].Contents.OfType<FunctionCallContent>());
        Assert.Equal("Paris", call.Arguments!["city"]!.ToString());
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
    }

    /// <summary>
    /// Streaming goes through the typed method: text updates arrive first, then the tool call as one
    /// function-call update, then usage and the finish reason — the same order the provider produced.
    /// </summary>
    [Fact]
    public async Task StreamingMapsTypedEventsInOrder()
    {
        var provider = new ScriptedLlmProvider([Text("Let me "), Text("check."), Call("c1", "get_weather", """{"city":"Paris"}"""), Done("tool_calls")]);
        var client = new LlmProviderChatClient(provider);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new MeaiChatMessage(ChatRole.User, "Weather?")], new ChatOptions { Tools = [Weather] }))
        {
            updates.Add(update);
        }

        Assert.Equal(1, provider.StreamingCalls);
        Assert.Equal(["Let me ", "check."], updates.Take(2).Select(u => u.Text));
        Assert.IsType<FunctionCallContent>(Assert.Single(updates[2].Contents));
        Assert.IsType<UsageContent>(Assert.Single(updates[3].Contents));
        Assert.Equal(ChatFinishReason.ToolCalls, updates[3].FinishReason);
    }

    /// <summary>
    /// A tool result coming back from the agent loop reaches the provider as a core tool message tied to the call id,
    /// after the assistant message that carried the call.
    /// </summary>
    [Fact]
    public async Task ToolResultsReachProviderAsToolMessages()
    {
        var provider = new ScriptedLlmProvider([Text("Sunny."), Done()]);
        var client = new LlmProviderChatClient(provider);
        MeaiChatMessage[] history =
        [
            new(ChatRole.User, "Weather?"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "get_weather", new Dictionary<string, object?> { ["city"] = "Paris" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "22 C in Paris")])
        ];

        await client.GetResponseAsync(history);

        var sent = provider.Calls[0];
        Assert.Equal("c1", Assert.Single(sent[1].ToolCalls!).Id);
        Assert.Equal("tool", sent[2].Role);
        Assert.Equal("c1", sent[2].ToolCallId);
        Assert.Equal("22 C in Paris", sent[2].Content);
    }

    /// <summary>
    /// Acceptance for adapter 1: an agent built with UseConfiguredLlm drives the TechieRag instance's own
    /// provider unchanged — the provider receives the knowledge-base tools, its tool call runs the
    /// search, and its answer is the agent's answer.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-017 AgentUsesConfiguredProviderUnchanged")]
    public async Task AgentUsesConfiguredProviderUnchanged()
    {
        var provider = new ScriptedLlmProvider(
            [Call("c1", "search_knowledge_base", """{"query":"refund window"}"""), Done("tool_calls")],
            [Text("30 days [S1]."), Done()]);
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create(provider)).UseConfiguredLlm().Build();

        var response = await agent.AskAsync("Return window?");

        Assert.Equal("30 days [S1].", response.Answer);
        Assert.Contains(provider.Options[0]!.Tools!, t => t.Name == "search_knowledge_base");
        Assert.Contains(AgentTestRag.RefundPassage, provider.Calls[1][^1].Content);
    }

    /// <summary>
    /// REQ-RAG-068 through the adapter: a streamed agent turn over a configured provider uses the typed
    /// streaming method for every model call and the answer text arrives as tokens.
    /// </summary>
    [Fact]
    public async Task AgentStreamsOverConfiguredProvider()
    {
        var provider = new ScriptedLlmProvider(
            [Call("c1", "search_knowledge_base", """{"query":"refund window"}"""), Done("tool_calls")],
            [Text("30 "), Text("days."), Done()]);
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create(provider)).UseConfiguredLlm().Build();

        var tokens = new List<string>();
        await foreach (var streamEvent in agent.AskStreamAsync("Return window?"))
        {
            if (streamEvent.Type == RagStreamEventType.Token) tokens.Add(streamEvent.Token!);
        }

        Assert.Equal(2, provider.StreamingCalls);
        Assert.Equal(["30 ", "days."], tokens);
    }
}
