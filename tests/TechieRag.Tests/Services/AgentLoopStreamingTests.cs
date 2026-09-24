using TechieRag.Models;
using TechieRag.Services;
using TechieRag.Tests.Llm;
using TechieRag.Tests.TestDoubles;
using Xunit;
using static TechieRag.Tests.TestDoubles.ScriptedEventLlmProvider;

namespace TechieRag.Tests.Services;

/// <summary>
/// Tests for <see cref="AgentLoopRunner.RunStreamAsync"/>, the streaming agent loop over typed events
/// (REQ-RAG-068 / BRD-111).
/// </summary>
public class AgentLoopStreamingTests
{
    /// <summary>
    /// A tool-using prompt streams its text as it arrives, then the tool call, then the tool's result
    /// produced by the <see cref="ToolRegistry"/>, then the answer text, then one completed event.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-068 StreamingRunStreamsTextAndExecutesToolThroughRegistry")]
    public async Task StreamingRunStreamsTextAndExecutesToolThroughRegistry()
    {
        var provider = new ScriptedEventLlmProvider(
            [Text("Let me "), Text("check."), Call("c1", "get_weather", """{"city":"Paris"}"""), Done("tool_calls")],
            [Text("Sunny, "), Text("22 C."), Done()]);
        var (registry, invocations) = WeatherRegistry();
        var runner = new AgentLoopRunner(provider, registry);

        var events = await CollectAsync(runner.RunStreamAsync([ChatMessage.User("Weather in Paris?")]));

        Assert.Equal(
            [
                AgentStreamEventKind.TextDelta, AgentStreamEventKind.TextDelta,
                AgentStreamEventKind.ToolCallRequested, AgentStreamEventKind.ToolExecuted,
                AgentStreamEventKind.TextDelta, AgentStreamEventKind.TextDelta,
                AgentStreamEventKind.Completed
            ],
            events.Select(e => e.Kind));
        Assert.Equal(["""{"city":"Paris"}"""], invocations);
        Assert.Equal("22 C in Paris", events[3].ToolResult!.Content);
        Assert.Equal("Sunny, 22 C.", events[^1].Response!.Content);
    }

    /// <summary>The second model call sees the assistant tool-call message and the registry's result, exactly as the non-streaming loop builds them.</summary>
    [Fact]
    public async Task StreamingRunFeedsToolResultBackToModel()
    {
        var provider = new ScriptedEventLlmProvider(
            [Call("c1", "get_weather", """{"city":"Paris"}"""), Done("tool_calls")],
            [Text("Sunny."), Done()]);
        var (registry, _) = WeatherRegistry();
        var messages = new List<ChatMessage> { ChatMessage.User("Weather in Paris?") };

        await CollectAsync(new AgentLoopRunner(provider, registry).RunStreamAsync(messages));

        var second = provider.Calls[1];
        Assert.Equal("c1", Assert.Single(second[1].ToolCalls!).Id);
        Assert.Equal("tool", second[2].Role);
        Assert.Equal("c1", second[2].ToolCallId);
        Assert.Equal("22 C in Paris", second[2].Content);
        Assert.Equal(3, messages.Count);
    }

    /// <summary>The trace is reported exactly as the non-streaming loop reports it.</summary>
    [Fact]
    public async Task StreamingRunReportsTheSameTrace()
    {
        var provider = new ScriptedEventLlmProvider(
            [Call("c1", "get_weather", """{"city":"Paris"}"""), Done("tool_calls")],
            [Text("Sunny."), Done()]);
        var (registry, _) = WeatherRegistry();
        var steps = new List<AgentStep>();

        await CollectAsync(new AgentLoopRunner(provider, registry).RunStreamAsync([ChatMessage.User("q")], progress: new SyncProgress<AgentStep>(steps.Add)));

        Assert.Equal([AgentStepKind.ToolCallRequested, AgentStepKind.ToolExecuted, AgentStepKind.FinalAnswer], steps.Select(s => s.Kind));
        Assert.Equal(2, steps[^1].Iteration);
    }

    /// <summary>Usage on the completed event is summed over every model call of the run.</summary>
    [Fact]
    public async Task StreamingRunSumsUsage()
    {
        var provider = new ScriptedEventLlmProvider(
            [Call("c1", "get_weather", "{}"), Done("tool_calls")],
            [Text("Done."), Done()]);
        var (registry, _) = WeatherRegistry();

        var events = await CollectAsync(new AgentLoopRunner(provider, registry).RunStreamAsync([ChatMessage.User("q")]));

        Assert.Equal(20, events[^1].Response!.Usage.InputTokens);
        Assert.Equal(10, events[^1].Response!.Usage.OutputTokens);
    }

    /// <summary>At the iteration cap the loop streams a forced final answer without tools and flags it.</summary>
    [Fact]
    public async Task StreamingRunStopsAtIterationCap()
    {
        var provider = new ScriptedEventLlmProvider(
            [Call("c1", "get_weather", "{}"), Done("tool_calls")],
            [Text("Best I can do."), Done()]);
        var (registry, _) = WeatherRegistry();

        var events = await CollectAsync(new AgentLoopRunner(provider, registry, maxIterations: 1).RunStreamAsync([ChatMessage.User("q")]));

        Assert.True(events[^1].MaxIterationsReached);
        Assert.Equal("Best I can do.", events[^1].Response!.Content);
        Assert.Null(provider.Options[1]!.Tools);
    }

    /// <summary>
    /// End to end over a real provider's wire format: LM Studio streams the tool call as fragments, the
    /// loop executes it through the registry, and the second streamed call's text arrives as deltas.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-068 StreamingRunWorksOverLmStudioWireFormat")]
    public async Task StreamingRunWorksOverLmStudioWireFormat()
    {
        var handler = new SequentialHandler(TypedStreamingTests.StreamFor("LmStudio"), TypedStreamingTests.OpenAIAnswerStream);
        var provider = TypedStreamingTests.Create("LmStudio", handler);
        var (registry, invocations) = WeatherRegistry();

        var events = await CollectAsync(new AgentLoopRunner(provider, registry).RunStreamAsync([ChatMessage.User("Weather in Paris?")]));

        Assert.Equal(["""{"city":"Paris"}"""], invocations);
        Assert.Equal("Sunny, 22 C.", events[^1].Response!.Content);
        Assert.Contains("\"tool_call_id\":\"call_1\"", handler.RequestBodies[1]);
    }

    private static (ToolRegistry Registry, List<string> Invocations) WeatherRegistry()
    {
        var invocations = new List<string>();
        var registry = new ToolRegistry();
        registry.Register("get_weather", "Gets the weather for a city.",
            """{"type":"object","properties":{"city":{"type":"string"}}}""",
            arguments =>
            {
                invocations.Add(arguments);
                return "22 C in Paris";
            });
        return (registry, invocations);
    }

    private static async Task<List<AgentStreamEvent>> CollectAsync(IAsyncEnumerable<AgentStreamEvent> stream)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var streamEvent in stream) events.Add(streamEvent);
        return events;
    }

    /// <summary>A progress sink that reports synchronously, so assertions see every step.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
