using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Services;
using TechieRag.Tests.TestDoubles;
using Xunit;
using static TechieRag.Tests.TestDoubles.ScriptedEventLlmProvider;

namespace TechieRag.Tests.Services;

/// <summary>
/// The agent loop runs a tool request a model writes as text instead of returning it as the answer
/// (REQ-RAG-159, Sevak TR-RAG-062).
/// </summary>
/// <remarks>
/// The scripted model answers the way Phi-3 mini answered Sevak through <c>TechieRag.Local</c> on 9 November 2026:
/// the request <c>{"name": "rag-search", "arguments": {"query": "…"}}</c> as plain text, with no structured call.
/// </remarks>
public class AgentLoopWrittenToolCallTests
{
    private const string WrittenSearch = """{"name": "rag-search", "arguments": {"query": "leave policy"}}""";

    /// <summary>
    /// Sevak's repro: the loop runs the written request through the tool handler, feeds the result back, and the
    /// final answer is the model's second reply, not the request text.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-159 BareJsonRequestRunsAsToolCall")]
    public async Task BareJsonRequestRunsAsToolCall()
    {
        var provider = new ScriptedEventLlmProvider([Text(WrittenSearch), Done()], [Text("Twenty days a year."), Done()]);
        var (registry, invocations) = SearchRegistry();
        var messages = new List<ChatMessage> { ChatMessage.User("How many leave days do I get?") };

        var response = await new AgentLoopRunner(provider, registry).RunAsync(messages);

        Assert.Equal("Twenty days a year.", response.Content);
        Assert.Equal(["""{"query": "leave policy"}"""], invocations);
        var second = provider.Calls[1];
        var call = Assert.Single(second[1].ToolCalls!);
        Assert.Equal("rag-search", call.Name);
        Assert.Equal("tool", second[2].Role);
        Assert.Equal(call.Id, second[2].ToolCallId);
        Assert.Equal("Leave: 20 days", second[2].Content);
    }

    /// <summary>The same request inside a <c>json</c> code fence runs too.</summary>
    [Fact(DisplayName = "REQ-RAG-159 FencedRequestRunsAsToolCall")]
    public async Task FencedRequestRunsAsToolCall()
    {
        var provider = new ScriptedEventLlmProvider([Text("```json\n" + WrittenSearch + "\n```"), Done()], [Text("Twenty days."), Done()]);
        var (registry, invocations) = SearchRegistry();

        var response = await new AgentLoopRunner(provider, registry).RunAsync([ChatMessage.User("q")]);

        Assert.Equal("Twenty days.", response.Content);
        Assert.Single(invocations);
    }

    /// <summary>Requests in <c>&lt;tool_call&gt;</c> tags run, two of them in one turn, with text before them and arguments written as a string.</summary>
    [Fact(DisplayName = "REQ-RAG-159 TaggedRequestsRunAsToolCalls")]
    public async Task TaggedRequestsRunAsToolCalls()
    {
        var written = "I will search.\n<tool_call>\n" + WrittenSearch + "\n</tool_call>\n<tool_call>{\"name\":\"rag-search\",\"arguments\":\"{\\\"query\\\":\\\"sick days\\\"}\"}</tool_call>";
        var provider = new ScriptedEventLlmProvider([Text(written), Done()], [Text("Done."), Done()]);
        var (registry, invocations) = SearchRegistry();

        var response = await new AgentLoopRunner(provider, registry).RunAsync([ChatMessage.User("q")]);

        Assert.Equal("Done.", response.Content);
        Assert.Equal(["""{"query": "leave policy"}""", """{"query":"sick days"}"""], invocations);
    }

    /// <summary>JSON naming no tool the loop offered stays the model's answer.</summary>
    [Fact(DisplayName = "REQ-RAG-159 JsonNamingNoOfferedToolStaysTheAnswer")]
    public async Task JsonNamingNoOfferedToolStaysTheAnswer()
    {
        const string answer = """{"name": "Ada Lovelace", "arguments": {"born": 1815}}""";
        var provider = new ScriptedEventLlmProvider([Text(answer), Done()]);
        var (registry, invocations) = SearchRegistry();

        var response = await new AgentLoopRunner(provider, registry).RunAsync([ChatMessage.User("q")]);

        Assert.Equal(answer, response.Content);
        Assert.Empty(invocations);
        Assert.Single(provider.Calls);
    }

    /// <summary>Written requests count against the loop's iteration limit exactly as structured calls do.</summary>
    [Fact(DisplayName = "REQ-RAG-159 WrittenRequestsObeyTheIterationLimit")]
    public async Task WrittenRequestsObeyTheIterationLimit()
    {
        var provider = new ScriptedEventLlmProvider([Text(WrittenSearch), Done()], [Text(WrittenSearch), Done()], [Text("Giving up."), Done()]);
        var (registry, invocations) = SearchRegistry();
        var steps = new List<AgentStep>();

        var response = await new AgentLoopRunner(provider, registry, maxIterations: 2)
            .RunAsync([ChatMessage.User("q")], progress: new SyncProgress<AgentStep>(steps.Add));

        Assert.Equal(2, invocations.Count);
        Assert.Equal("Giving up.", response.Content);
        Assert.Equal(AgentStepKind.MaxIterationsReached, steps[^1].Kind);
        Assert.Null(provider.Options[2]!.Tools);
    }

    /// <summary>A written request goes through the handler the loop was given, so a guard in it refuses it and the refusal is fed back.</summary>
    [Fact(DisplayName = "REQ-RAG-159 WrittenRequestGoesThroughTheGuard")]
    public async Task WrittenRequestGoesThroughTheGuard()
    {
        var provider = new ScriptedEventLlmProvider([Text(WrittenSearch), Done()], [Text("I cannot search."), Done()]);
        var (registry, invocations) = SearchRegistry();
        var guard = new RefusingGuard(registry);

        var response = await new AgentLoopRunner(provider, guard).RunAsync([ChatMessage.User("q")]);

        Assert.Equal("I cannot search.", response.Content);
        Assert.Equal(["rag-search"], guard.Refused);
        Assert.Empty(invocations);
        Assert.Equal("blocked by the egress gate", provider.Calls[1][2].Content);
    }

    /// <summary>The streaming loop runs the written request too and never shows its text as answer text.</summary>
    [Fact(DisplayName = "REQ-RAG-159 StreamingRunHidesAndRunsWrittenRequest")]
    public async Task StreamingRunHidesAndRunsWrittenRequest()
    {
        var provider = new ScriptedEventLlmProvider(
            [Text("{\"name\": \"rag-search\", "), Text("\"arguments\": {\"query\": \"leave policy\"}}"), Done()],
            [Text("Twenty "), Text("days."), Done()]);
        var (registry, invocations) = SearchRegistry();

        var events = new List<AgentStreamEvent>();
        await foreach (var streamEvent in new AgentLoopRunner(provider, registry).RunStreamAsync([ChatMessage.User("q")]))
        {
            events.Add(streamEvent);
        }

        Assert.Single(invocations);
        Assert.DoesNotContain(events, e => e.Kind == AgentStreamEventKind.TextDelta && e.Text!.Contains("rag-search", StringComparison.Ordinal));
        Assert.Contains(events, e => e.Kind == AgentStreamEventKind.ToolCallRequested && e.ToolCall!.Name == "rag-search");
        Assert.Equal("Twenty days.", string.Concat(events.Where(e => e.Kind == AgentStreamEventKind.TextDelta).Select(e => e.Text)));
        Assert.Equal("Twenty days.", events[^1].Response!.Content);
    }

    /// <summary>A plain answer still streams piece by piece; only text that starts like a request is held back.</summary>
    [Fact(DisplayName = "REQ-RAG-159 StreamingPlainAnswerStillStreams")]
    public async Task StreamingPlainAnswerStillStreams()
    {
        var provider = new ScriptedEventLlmProvider([Text("Twenty "), Text("days."), Done()]);
        var (registry, _) = SearchRegistry();

        var deltas = new List<string>();
        await foreach (var streamEvent in new AgentLoopRunner(provider, registry).RunStreamAsync([ChatMessage.User("q")]))
        {
            if (streamEvent.Kind == AgentStreamEventKind.TextDelta) deltas.Add(streamEvent.Text!);
        }

        Assert.Equal(["Twenty ", "days."], deltas);
    }

    private static (ToolRegistry Registry, List<string> Invocations) SearchRegistry()
    {
        var invocations = new List<string>();
        var registry = new ToolRegistry();
        registry.Register("rag-search", "Searches the user's documents.",
            """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}""",
            arguments =>
            {
                invocations.Add(arguments);
                return "Leave: 20 days";
            });
        return (registry, invocations);
    }

    /// <summary>Reports a step at once, on the calling thread.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>Stands in for a guard such as the egress gate: offers the inner tools and refuses every call.</summary>
    private sealed class RefusingGuard(IToolHandler inner) : IToolHandler
    {
        public List<string> Refused { get; } = [];

        public IReadOnlyList<ToolDefinition> ToolDefinitions => inner.ToolDefinitions;

        public Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
        {
            Refused.Add(toolCall.Name);
            return Task.FromResult(new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = "blocked by the egress gate",
                IsSuccess = false,
                ErrorMessage = "blocked"
            });
        }
    }
}
