using TechieRag.Local.Tests.TestDoubles;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// A local reasoning model's leading <c>&lt;think&gt;</c> block is split out of its answer (REQ-RAG-142).
/// </summary>
/// <remarks>The pieces are what Qwen3 and DeepSeek-R1 builds generate token by token, tags cut across pieces.</remarks>
public sealed class LocalThinkingTests
{
    private static readonly string[] ThinkingPieces = ["<th", "ink>", "The user", " greets me.", "</th", "ink>", "\n\n", "Hello", " there!"];

    private readonly FakeLocalLlmRuntime runtime = new() { Script = (_, _) => ThinkingPieces };

    /// <summary>
    /// Streamed, the block arrives as thinking events before the answer, the answer text holds only the
    /// answer, and the thinking tokens are reported as reasoning tokens.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-142 LeadingThinkBlockStreamsAsThinking")]
    public async Task LeadingThinkBlockStreamsAsThinking()
    {
        using var provider = FakeProviderFactory.Create(runtime);

        var events = new List<LlmStreamEvent>();
        await foreach (var streamEvent in provider.ChatStreamEventsAsync([ChatMessage.User("Hi")])) events.Add(streamEvent);

        Assert.Equal("The user greets me.", Concat(events, LlmStreamEventKind.Thinking));
        Assert.Equal("Hello there!", Concat(events, LlmStreamEventKind.TextDelta));
        Assert.True(events.FindLastIndex(e => e.Kind == LlmStreamEventKind.Thinking) < events.FindIndex(e => e.Kind == LlmStreamEventKind.TextDelta));
        Assert.Equal(LlmStreamEventKind.Completed, events[^1].Kind);
        Assert.Equal(FakeLocalModel.Count("The user greets me."), events[^1].Usage!.ReasoningTokens);
    }

    /// <summary>Whole, the block is in <see cref="LlmResponse.Thinking"/> and the answer is clean.</summary>
    [Fact(DisplayName = "REQ-RAG-142 ChatSplitsLeadingThinkBlock")]
    public async Task ChatSplitsLeadingThinkBlock()
    {
        using var provider = FakeProviderFactory.Create(runtime);

        var response = await provider.ChatAsync([ChatMessage.User("Hi")]);

        Assert.Equal("Hello there!", response.Content);
        Assert.Equal("The user greets me.", response.Thinking);
    }

    /// <summary>With tools, the block is split off before the tool call is parsed, so the call still comes through.</summary>
    [Fact(DisplayName = "REQ-RAG-142 ThinkBlockBeforeToolCallIsSplit")]
    public async Task ThinkBlockBeforeToolCallIsSplit()
    {
        runtime.Script = (_, _) => ["<think>", "Need the weather.", "</think>\n", "<tool_call>\n", """{"name":"get_weather","arguments":{"city":"Paris"}}""", "\n</tool_call>"];
        using var provider = FakeProviderFactory.Create(runtime, supportsToolCalling: true);
        var options = new LlmCompletionOptions
        {
            Tools = [new ToolDefinition { Name = "get_weather", Description = "Gets the weather.", ParametersSchema = """{"type":"object"}""" }]
        };

        var response = await provider.ChatAsync([ChatMessage.User("Weather in Paris?")], options);

        Assert.Equal("Need the weather.", response.Thinking);
        Assert.Equal("get_weather", Assert.Single(response.ToolCalls!).Name);
        Assert.Null(response.Content);
    }

    /// <summary>A model that does not think sends no thinking event.</summary>
    [Fact(DisplayName = "REQ-RAG-142 PlainAnswerHasNoThinking")]
    public async Task PlainAnswerHasNoThinking()
    {
        using var provider = FakeProviderFactory.Create(new FakeLocalLlmRuntime());

        var events = new List<LlmStreamEvent>();
        await foreach (var streamEvent in provider.ChatStreamEventsAsync([ChatMessage.User("Hi")])) events.Add(streamEvent);

        Assert.DoesNotContain(events, e => e.Kind == LlmStreamEventKind.Thinking);
        Assert.Equal("Hello there.", Concat(events, LlmStreamEventKind.TextDelta));
        Assert.Equal(0, events[^1].Usage!.ReasoningTokens);
    }

    private static string Concat(IEnumerable<LlmStreamEvent> events, LlmStreamEventKind kind) =>
        string.Concat(events.Where(e => e.Kind == kind).Select(e => e.Text));
}
