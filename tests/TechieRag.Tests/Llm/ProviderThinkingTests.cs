using System.Text.Json;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;
using TechieRag.Services;
using TechieRag.Tests.Llm.Subscription;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Thinking text from the providers that do not speak the OpenAI chat-completions dialect: Anthropic
/// (REQ-RAG-139), Gemini (REQ-RAG-140), Ollama (REQ-RAG-141) and the ChatGPT subscription (REQ-RAG-143).
/// </summary>
/// <remarks>
/// Each recorded reply follows the shape the service documents for a reasoning model. The rules every test
/// checks: thinking never reaches the answer text, it arrives as <see cref="LlmStreamEventKind.Thinking"/>
/// events or <see cref="LlmResponse.Thinking"/>, and <see cref="LlmStreamEventKind.Completed"/> stays last.
/// </remarks>
public class ProviderThinkingTests
{
    private static readonly IReadOnlyList<ChatMessage> Conversation = [ChatMessage.User("say hello")];

    private const string AnthropicStream = """
        event: message_start
        data: {"type":"message_start","message":{"model":"claude-sonnet-4-5-20250929","usage":{"input_tokens":9,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"The user wants "}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"a greeting."}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig-abc"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: content_block_start
        data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Hello!"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":1}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":40}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    private const string AnthropicToolReply = """
        {"model":"claude-sonnet-4-5-20250929","stop_reason":"tool_use",
         "content":[{"type":"thinking","thinking":"I need the weather.","signature":"sig-1"},
                    {"type":"redacted_thinking","data":"enc-2"},
                    {"type":"tool_use","id":"t1","name":"get_weather","input":{"city":"Paris"}}],
         "usage":{"input_tokens":20,"output_tokens":30}}
        """;

    /// <summary>
    /// A streamed Anthropic reply with a thinking block yields its fragments as thinking events before the
    /// answer, and the completed event carries the signed block for the next call.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-139 AnthropicThinkingBlockStreamsAsThinking")]
    public async Task AnthropicThinkingBlockStreamsAsThinking()
    {
        var events = await CollectAsync(TypedStreamingTests.Create("Anthropic", new CapturingHandler(AnthropicStream)));

        Assert.Equal(
            [LlmStreamEventKind.Thinking, LlmStreamEventKind.Thinking, LlmStreamEventKind.TextDelta, LlmStreamEventKind.Completed],
            events.Select(e => e.Kind));
        Assert.Equal("The user wants a greeting.", Concat(events, LlmStreamEventKind.Thinking));
        Assert.Equal("Hello!", Concat(events, LlmStreamEventKind.TextDelta));
        Assert.Contains("sig-abc", events[^1].ProviderState);
    }

    /// <summary>
    /// A whole Anthropic reply returns its thinking text in <see cref="LlmResponse.Thinking"/>; the redacted
    /// block has no readable text and is kept only for the next call.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-139 AnthropicChatReturnsThinking")]
    public async Task AnthropicChatReturnsThinking()
    {
        var llm = TypedStreamingTests.Create("Anthropic", new CapturingHandler(AnthropicToolReply));

        var response = await llm.ChatAsync(Conversation);

        Assert.Null(response.Content);
        Assert.Equal("I need the weather.", response.Thinking);
        Assert.Equal("get_weather", Assert.Single(response.ToolCalls!).Name);
        Assert.Contains("enc-2", response.ProviderState);
    }

    /// <summary>
    /// A tool-using turn with thinking on sends the earlier turn's thinking blocks back first and unchanged,
    /// as the service requires, and never as answer text.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-139 AnthropicToolTurnSendsThinkingBack")]
    public async Task AnthropicToolTurnSendsThinkingBack()
    {
        var first = await TypedStreamingTests.Create("Anthropic", new CapturingHandler(AnthropicToolReply)).ChatAsync(Conversation);
        var handler = new CapturingHandler("""{"content":[{"type":"text","text":"22 C."}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""");
        var llm = TypedStreamingTests.Create("Anthropic", handler);

        await llm.ChatAsync([.. Conversation, first.ToChatMessage(), ChatMessage.Tool("t1", "22 C")], new LlmCompletionOptions { MaxReasoningTokens = 2048 });

        using var body = JsonDocument.Parse(handler.CapturedBody!);
        var assistant = body.RootElement.GetProperty("messages")[1].GetProperty("content");
        Assert.Equal(["thinking", "redacted_thinking", "tool_use"], assistant.EnumerateArray().Select(b => b.GetProperty("type").GetString()));
        Assert.Equal("sig-1", assistant[0].GetProperty("signature").GetString());
        Assert.Equal(2048, body.RootElement.GetProperty("thinking").GetProperty("budget_tokens").GetInt32());
        Assert.True(body.RootElement.GetProperty("max_tokens").GetInt32() > 2048);
    }

    /// <summary>Without a reasoning budget or effort the request asks for no thinking, so the reply carries none.</summary>
    [Fact(DisplayName = "REQ-RAG-139 AnthropicAsksForThinkingOnlyWhenTold")]
    public async Task AnthropicAsksForThinkingOnlyWhenTold()
    {
        var handler = new CapturingHandler("""{"content":[{"type":"text","text":"Hi."}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""");
        var llm = TypedStreamingTests.Create("Anthropic", handler);

        var response = await llm.ChatAsync(Conversation, new LlmCompletionOptions { Temperature = 0.2f });

        Assert.DoesNotContain("\"thinking\"", handler.CapturedBody);
        Assert.Contains("\"temperature\"", handler.CapturedBody);
        Assert.Null(response.Thinking);
        Assert.Null(response.ProviderState);
    }

    /// <summary>
    /// A streamed Gemini reply with a thought part yields it as thinking, the answer holds only the answer
    /// text, and the thought token count is reported as reasoning tokens inside the output tokens.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-140 GeminiThoughtPartsStreamAsThinking")]
    public async Task GeminiThoughtPartsStreamAsThinking()
    {
        const string stream = """
            data: {"candidates":[{"content":{"role":"model","parts":[{"text":"The user wants a greeting.","thought":true}]}}]}

            data: {"candidates":[{"content":{"role":"model","parts":[{"text":"Hello!"}]}}],"usageMetadata":{"promptTokenCount":9,"candidatesTokenCount":3,"thoughtsTokenCount":22,"totalTokenCount":34}}

            """;

        var events = await CollectAsync(TypedStreamingTests.Create("Gemini", new CapturingHandler(stream)));

        Assert.Equal([LlmStreamEventKind.Thinking, LlmStreamEventKind.TextDelta, LlmStreamEventKind.Completed], events.Select(e => e.Kind));
        Assert.Equal("The user wants a greeting.", Concat(events, LlmStreamEventKind.Thinking));
        Assert.Equal("Hello!", Concat(events, LlmStreamEventKind.TextDelta));
        Assert.Equal(22, events[^1].Usage!.ReasoningTokens);
        Assert.Equal(25, events[^1].Usage!.OutputTokens);
    }

    /// <summary>A whole Gemini reply returns the thought part in <see cref="LlmResponse.Thinking"/>, not in the answer.</summary>
    [Fact(DisplayName = "REQ-RAG-140 GeminiChatReturnsThoughtAsThinking")]
    public async Task GeminiChatReturnsThoughtAsThinking()
    {
        const string body = """
            {"candidates":[{"content":{"role":"model","parts":[{"text":"The user wants a greeting.","thought":true},{"text":"Hello!"}]}}],
             "usageMetadata":{"promptTokenCount":9,"candidatesTokenCount":3,"thoughtsTokenCount":22,"totalTokenCount":34}}
            """;
        var llm = TypedStreamingTests.Create("Gemini", new CapturingHandler(body));

        var response = await llm.ChatAsync(Conversation);

        Assert.Equal("Hello!", response.Content);
        Assert.Equal("The user wants a greeting.", response.Thinking);
        Assert.Equal(22, response.Usage.ReasoningTokens);
    }

    /// <summary>A reasoning budget asks Gemini for its thoughts; disabling reasoning sends a zero budget.</summary>
    [Fact(DisplayName = "REQ-RAG-140 GeminiAsksForThoughtsWhenTold")]
    public async Task GeminiAsksForThoughtsWhenTold()
    {
        const string body = """{"candidates":[{"content":{"parts":[{"text":"Hi."}]}}]}""";
        var asked = new CapturingHandler(body);
        var off = new CapturingHandler(body);

        await TypedStreamingTests.Create("Gemini", asked).ChatAsync(Conversation, new LlmCompletionOptions { MaxReasoningTokens = 2048 });
        await TypedStreamingTests.Create("Gemini", off).ChatAsync(Conversation, new LlmCompletionOptions { DisableReasoning = true });

        var thinking = JsonDocument.Parse(asked.CapturedBody!).RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig");
        Assert.True(thinking.GetProperty("includeThoughts").GetBoolean());
        Assert.Equal(2048, thinking.GetProperty("thinkingBudget").GetInt32());
        Assert.Equal(0, JsonDocument.Parse(off.CapturedBody!).RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
    }

    /// <summary>A streamed Ollama reply with a thinking field yields it as thinking before the answer.</summary>
    [Fact(DisplayName = "REQ-RAG-141 OllamaThinkingFieldStreamsAsThinking")]
    public async Task OllamaThinkingFieldStreamsAsThinking()
    {
        const string stream = """
            {"model":"qwen3","message":{"role":"assistant","content":"","thinking":"The user wants "},"done":false}
            {"model":"qwen3","message":{"role":"assistant","content":"","thinking":"a greeting."},"done":false}
            {"model":"qwen3","message":{"role":"assistant","content":"Hello!"},"done":false}
            {"model":"qwen3","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop","prompt_eval_count":9,"eval_count":30}
            """;

        var events = await CollectAsync(TypedStreamingTests.Create("Ollama", new CapturingHandler(stream)));

        Assert.Equal(
            [LlmStreamEventKind.Thinking, LlmStreamEventKind.Thinking, LlmStreamEventKind.TextDelta, LlmStreamEventKind.Completed],
            events.Select(e => e.Kind));
        Assert.Equal("The user wants a greeting.", Concat(events, LlmStreamEventKind.Thinking));
        Assert.Equal("Hello!", Concat(events, LlmStreamEventKind.TextDelta));
    }

    /// <summary>A streamed Ollama answer that opens with a <c>&lt;think&gt;</c> block, tags cut across chunks, is split.</summary>
    [Fact(DisplayName = "REQ-RAG-141 OllamaLeadingThinkBlockStreamsAsThinking")]
    public async Task OllamaLeadingThinkBlockStreamsAsThinking()
    {
        const string stream = """
            {"message":{"role":"assistant","content":"<thi"},"done":false}
            {"message":{"role":"assistant","content":"nk>The user greets me.</thi"},"done":false}
            {"message":{"role":"assistant","content":"nk>\n\nHello there!"},"done":false}
            {"message":{"role":"assistant","content":""},"done":true,"done_reason":"stop","prompt_eval_count":9,"eval_count":20}
            """;

        var events = await CollectAsync(TypedStreamingTests.Create("Ollama", new CapturingHandler(stream)));

        Assert.Equal("The user greets me.", Concat(events, LlmStreamEventKind.Thinking));
        Assert.Equal("Hello there!", Concat(events, LlmStreamEventKind.TextDelta));
        Assert.Equal(LlmStreamEventKind.Completed, events[^1].Kind);
    }

    /// <summary>A whole Ollama reply returns its thinking field, or a leading block, in <see cref="LlmResponse.Thinking"/>.</summary>
    /// <param name="message">The recorded <c>message</c> object.</param>
    [Theory(DisplayName = "REQ-RAG-141 OllamaChatReturnsThinking")]
    [InlineData("""{"role":"assistant","content":"Hello!","thinking":"The user wants a greeting."}""")]
    [InlineData("""{"role":"assistant","content":"<think>The user wants a greeting.</think>\n\nHello!"}""")]
    public async Task OllamaChatReturnsThinking(string message)
    {
        var llm = TypedStreamingTests.Create("Ollama", new CapturingHandler($$"""{"message":{{message}},"done":true,"done_reason":"stop","prompt_eval_count":9,"eval_count":30}"""));

        var response = await llm.ChatAsync(Conversation);

        Assert.Equal("Hello!", response.Content);
        Assert.Equal("The user wants a greeting.", response.Thinking);
    }

    /// <summary>The app's reasoning switches map to Ollama's <c>think</c> setting; with neither set it is left out.</summary>
    [Fact(DisplayName = "REQ-RAG-141 OllamaThinkFollowsReasoningSwitches")]
    public async Task OllamaThinkFollowsReasoningSwitches()
    {
        const string body = """{"message":{"role":"assistant","content":"Hi."},"done":true}""";
        var on = new CapturingHandler(body);
        var off = new CapturingHandler(body);
        var unset = new CapturingHandler(body);

        await TypedStreamingTests.Create("Ollama", on).ChatAsync(Conversation, new LlmCompletionOptions { ReasoningEffort = "high" });
        await TypedStreamingTests.Create("Ollama", off).ChatAsync(Conversation, new LlmCompletionOptions { DisableReasoning = true });
        await TypedStreamingTests.Create("Ollama", unset).ChatAsync(Conversation);

        Assert.True(JsonDocument.Parse(on.CapturedBody!).RootElement.GetProperty("think").GetBoolean());
        Assert.False(JsonDocument.Parse(off.CapturedBody!).RootElement.GetProperty("think").GetBoolean());
        Assert.False(JsonDocument.Parse(unset.CapturedBody!).RootElement.TryGetProperty("think", out _));
    }

    /// <summary>
    /// A subscription stream with reasoning summary text yields it as thinking, the answer is clean, and the
    /// reasoning token count is reported; the request asked for the summary because the app set an effort.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-143 SubscriptionSummaryStreamsAsThinking")]
    public async Task SubscriptionSummaryStreamsAsThinking()
    {
        var server = new FakeChatGptServer { ResponseStream = SummaryStream() };
        server.Authorise();
        using var llm = new ChatGptSubscriptionLlmProvider((_, _) => Task.CompletedTask, new ChatGptSubscriptionOptions { Handler = server, Delay = (_, _) => Task.CompletedTask });

        var events = new List<LlmStreamEvent>();
        await foreach (var streamEvent in llm.ChatStreamEventsAsync(Conversation, new LlmCompletionOptions { ReasoningEffort = "medium" })) events.Add(streamEvent);

        Assert.Equal("**Greeting**\n\nThe user wants a hello.", Concat(events, LlmStreamEventKind.Thinking));
        Assert.Equal("Hello!", Concat(events, LlmStreamEventKind.TextDelta));
        Assert.Equal(LlmStreamEventKind.Completed, events[^1].Kind);
        Assert.Equal(18, events[^1].Usage!.ReasoningTokens);
        var reasoning = JsonDocument.Parse(server.BackendBodies.Single()).RootElement.GetProperty("reasoning");
        Assert.Equal(("medium", "auto"), (reasoning.GetProperty("effort").GetString(), reasoning.GetProperty("summary").GetString()));
    }

    /// <summary>A whole subscription reply returns the summary in <see cref="LlmResponse.Thinking"/>; no effort asks for none.</summary>
    [Fact(DisplayName = "REQ-RAG-143 SubscriptionChatReturnsSummaryAsThinking")]
    public async Task SubscriptionChatReturnsSummaryAsThinking()
    {
        var server = new FakeChatGptServer { ResponseStream = SummaryStream() };
        server.Authorise();
        using var llm = new ChatGptSubscriptionLlmProvider((_, _) => Task.CompletedTask, new ChatGptSubscriptionOptions { Handler = server, Delay = (_, _) => Task.CompletedTask });

        var response = await llm.ChatAsync(Conversation);

        Assert.Equal("Hello!", response.Content);
        Assert.Equal("**Greeting**\n\nThe user wants a hello.", response.Thinking);
        Assert.False(JsonDocument.Parse(server.BackendBodies.Single()).RootElement.TryGetProperty("reasoning", out _));
    }

    /// <summary>
    /// The agent loop keeps a streamed turn's provider state on the assistant message it appends, so the
    /// Anthropic thinking blocks reach the next call of a tool-using run (REQ-RAG-139).
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-139 StreamedAgentTurnKeepsProviderState")]
    public async Task StreamedAgentTurnKeepsProviderState()
    {
        var llm = new StateScriptedProvider();
        var tools = new ToolRegistry();
        tools.Register("get_weather", "Weather.", """{"type":"object"}""", _ => "22 C");
        var runner = new AgentLoopRunner(llm, tools);

        await foreach (var _ in runner.RunStreamAsync([ChatMessage.User("Weather?")], new LlmCompletionOptions { Tools = tools.ToolDefinitions })) { }

        Assert.Equal("state-1", llm.SecondCallMessages!.Single(m => m.Role == "assistant").ProviderState);
    }

    private static string SummaryStream()
    {
        var events = new object[]
        {
            new { type = "response.reasoning_summary_part.added", summary_index = 0 },
            new { type = "response.reasoning_summary_text.delta", delta = "**Greeting**" },
            new { type = "response.reasoning_summary_part.added", summary_index = 1 },
            new { type = "response.reasoning_summary_text.delta", delta = "The user wants a hello." },
            new { type = "response.output_text.delta", delta = "Hello!" },
            new { type = "response.completed", response = new { usage = new { input_tokens = 12, output_tokens = 25, output_tokens_details = new { reasoning_tokens = 18 } } } }
        };
        return string.Concat(events.Select(e => $"data: {JsonSerializer.Serialize(e)}\n\n"));
    }

    private static async Task<List<LlmStreamEvent>> CollectAsync(ILlmProvider llm)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var streamEvent in llm.ChatStreamEventsAsync(Conversation)) events.Add(streamEvent);
        return events;
    }

    private static string Concat(IEnumerable<LlmStreamEvent> events, LlmStreamEventKind kind) =>
        string.Concat(events.Where(e => e.Kind == kind).Select(e => e.Text));

    /// <summary>Asks for a tool with provider state on the first call, answers on the second, and records it.</summary>
    private sealed class StateScriptedProvider : ILlmProvider
    {
        private int calls;

        public IReadOnlyList<ChatMessage>? SecondCallMessages { get; private set; }
        public string Name => "scripted";
        public string ModelName => "scripted";
        public string? Route => null;
        public bool SupportsToolCalling => true;
        public bool SupportsStreaming => true;
        public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted { add { } remove { } }

        public async IAsyncEnumerable<LlmStreamEvent> ChatStreamEventsAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var usage = new TokenUsage { ModelName = ModelName, ProviderName = Name };
            if (++calls == 1)
            {
                yield return LlmStreamEvent.FromThinking("I need the weather.");
                yield return LlmStreamEvent.FromToolCall(new ToolCall { Id = "t1", Name = "get_weather", ArgumentsJson = "{}" });
                yield return LlmStreamEvent.FromCompleted(usage, "tool_calls", ModelName, "state-1");
                yield break;
            }

            SecondCallMessages = [.. messages];
            yield return LlmStreamEvent.FromText("22 C.");
            yield return LlmStreamEvent.FromCompleted(usage, "stop", ModelName);
        }

        public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class => throw new NotSupportedException();
        public int EstimateTokenCount(string text) => text.Length / 4;
    }
}
