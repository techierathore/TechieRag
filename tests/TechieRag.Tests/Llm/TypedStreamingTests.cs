using System.Text.Json;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Wire-format tests for the typed streaming method <see cref="ILlmProvider.ChatStreamEventsAsync"/>
/// on all six built-in providers (REQ-RAG-067 / BRD-110).
/// </summary>
/// <remarks>
/// Each provider is fed its own vendor's recorded stream shape for one tool-using turn — some text,
/// then a get_weather call — through a stub handler. OpenAI-style services and Anthropic send the tool
/// arguments as fragments; Ollama and Gemini send the call whole. Either way the provider must yield
/// text deltas, then one assembled tool call, then a completed event, in that order.
/// </remarks>
public class TypedStreamingTests
{
    /// <summary>The six built-in providers.</summary>
    public static TheoryData<string> Providers => new() { "LmStudio", "OpenAICompatible", "AzureAIFoundry", "Ollama", "Gemini", "Anthropic" };

    private static readonly IReadOnlyList<ChatMessage> Conversation = [ChatMessage.User("Weather in Paris?")];

    private static readonly LlmCompletionOptions ToolOptions = new()
    {
        Tools =
        [
            new ToolDefinition
            {
                Name = "get_weather",
                Description = "Gets the weather for a city.",
                ParametersSchema = """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}"""
            }
        ]
    };

    /// <summary>
    /// Streaming a tool-using turn yields text deltas first, then exactly one tool-call event with the
    /// assembled name, arguments and id, then one completed event last — on every provider.
    /// </summary>
    /// <param name="provider">The provider under test.</param>
    [Theory(DisplayName = "REQ-RAG-067 TypedStreamOrdersTextToolCallCompleted")]
    [MemberData(nameof(Providers))]
    public async Task TypedStreamOrdersTextToolCallCompleted(string provider)
    {
        var events = await CollectAsync(Create(provider, new CapturingHandler(StreamFor(provider))));

        var kinds = events.Select(e => e.Kind).ToList();
        Assert.Equal(LlmStreamEventKind.Completed, kinds[^1]);
        Assert.Equal(1, kinds.Count(k => k == LlmStreamEventKind.ToolCall));
        var toolIndex = kinds.IndexOf(LlmStreamEventKind.ToolCall);
        Assert.True(toolIndex >= 2, "Two text deltas must precede the tool call.");
        Assert.All(kinds.Take(toolIndex), k => Assert.Equal(LlmStreamEventKind.TextDelta, k));
        Assert.Equal("Let me check.", string.Concat(events.Take(toolIndex).Select(e => e.Text)));
    }

    /// <summary>The tool call arrives whole: name, and arguments that parse to the city the model chose.</summary>
    /// <param name="provider">The provider under test.</param>
    [Theory(DisplayName = "REQ-RAG-067 TypedStreamAssemblesToolArguments")]
    [MemberData(nameof(Providers))]
    public async Task TypedStreamAssemblesToolArguments(string provider)
    {
        var events = await CollectAsync(Create(provider, new CapturingHandler(StreamFor(provider))));

        var call = Assert.Single(events, e => e.Kind == LlmStreamEventKind.ToolCall).ToolCall!;
        Assert.Equal("get_weather", call.Name);
        Assert.False(string.IsNullOrEmpty(call.Id));
        using var arguments = JsonDocument.Parse(call.ArgumentsJson);
        Assert.Equal("Paris", arguments.RootElement.GetProperty("city").GetString());
    }

    /// <summary>The completed event carries the reported usage and a tool-calls finish reason.</summary>
    /// <param name="provider">The provider under test.</param>
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task TypedStreamCompletesWithUsage(string provider)
    {
        var events = await CollectAsync(Create(provider, new CapturingHandler(StreamFor(provider))));

        var completed = events[^1];
        Assert.Equal(12, completed.Usage!.InputTokens);
        Assert.Equal(7, completed.Usage.OutputTokens);
        Assert.Equal("tool_calls", completed.FinishReason);
    }

    /// <summary>
    /// <c>ChatStreamAsync</c> is unchanged in behaviour: over the same stream it yields only the text
    /// fragments, never the tool call or the completion.
    /// </summary>
    /// <param name="provider">The provider under test.</param>
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task TextStreamYieldsOnlyText(string provider)
    {
        var llm = Create(provider, new CapturingHandler(StreamFor(provider)));

        var tokens = new List<string>();
        await foreach (var token in llm.ChatStreamAsync(Conversation, ToolOptions)) tokens.Add(token);

        Assert.Equal(["Let me ", "check."], tokens);
    }

    /// <summary>The tools are still sent on the streaming request, so the model can call them.</summary>
    /// <param name="provider">The provider under test.</param>
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task TypedStreamSendsTools(string provider)
    {
        var handler = new CapturingHandler(StreamFor(provider));
        await CollectAsync(Create(provider, handler));

        Assert.Contains("get_weather", handler.CapturedBody);
    }

    /// <summary>
    /// A provider written before the typed method existed still gets it, through the interface default:
    /// with tools supplied it asks the whole response and replays it as text, tool call, completed.
    /// </summary>
    [Fact]
    public async Task LegacyProviderGetsDefaultTypedStream()
    {
        ILlmProvider legacy = new LegacyToolProvider();

        var events = new List<LlmStreamEvent>();
        await foreach (var streamEvent in legacy.ChatStreamEventsAsync(Conversation, ToolOptions)) events.Add(streamEvent);

        Assert.Equal([LlmStreamEventKind.TextDelta, LlmStreamEventKind.ToolCall, LlmStreamEventKind.Completed], events.Select(e => e.Kind));
        Assert.Equal("get_weather", events[1].ToolCall!.Name);
    }

    private static async Task<List<LlmStreamEvent>> CollectAsync(ILlmProvider llm)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var streamEvent in llm.ChatStreamEventsAsync(Conversation, ToolOptions)) events.Add(streamEvent);
        return events;
    }

    /// <summary>Creates a provider over a stub handler.</summary>
    internal static ILlmProvider Create(string provider, HttpMessageHandler handler) => provider switch
    {
        "LmStudio" => new LmStudioLlmProvider(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234") }, "qwen3-8b"),
        "OpenAICompatible" => new OpenAICompatibleLlmProvider(new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test") }, "gpt-4o-mini"),
        "AzureAIFoundry" => new AzureAIFoundryLlmProvider(new HttpClient(handler) { BaseAddress = new Uri("https://foundry.test") }, "gpt-4o"),
        "Ollama" => new OllamaLlmProvider(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") }, "llama3.2"),
        "Gemini" => new GoogleGeminiLlmProvider(new HttpClient(handler) { BaseAddress = new Uri("https://gemini.test") }, "key", "gemini-2.0-flash"),
        "Anthropic" => new AnthropicLlmProvider(new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") }, "claude-sonnet-4-5-20250929"),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    /// <summary>The recorded stream shape of one tool-using turn for a provider.</summary>
    internal static string StreamFor(string provider) => provider switch
    {
        "Ollama" => OllamaStream,
        "Gemini" => GeminiStream,
        "Anthropic" => AnthropicStream,
        _ => OpenAIStream
    };

    /// <summary>The recorded OpenAI-style final-answer stream (no tools), for a second loop iteration.</summary>
    internal const string OpenAIAnswerStream = """
        data: {"choices":[{"delta":{"content":"Sunny, "}}]}

        data: {"choices":[{"delta":{"content":"22 C."},"finish_reason":"stop"}]}

        data: {"choices":[],"usage":{"prompt_tokens":30,"completion_tokens":4,"total_tokens":34}}

        data: [DONE]

        """;

    private const string OpenAIStream = """
        data: {"choices":[{"delta":{"role":"assistant","content":"Let me "}}]}

        data: {"choices":[{"delta":{"content":"check."}}]}

        data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"get_weather","arguments":""}}]}}]}

        data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"city\":"}}]}}]}

        data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"Paris\"}"}}]}}]}

        data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}

        data: {"choices":[],"usage":{"prompt_tokens":12,"completion_tokens":7,"total_tokens":19}}

        data: [DONE]

        """;

    private const string OllamaStream = """
        {"message":{"role":"assistant","content":"Let me "},"done":false}
        {"message":{"role":"assistant","content":"check."},"done":false}
        {"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"get_weather","arguments":{"city":"Paris"}}}]},"done":false}
        {"message":{"role":"assistant","content":""},"done":true,"prompt_eval_count":12,"eval_count":7}
        """;

    private const string GeminiStream = """
        data: {"candidates":[{"content":{"parts":[{"text":"Let me "}],"role":"model"}}]}

        data: {"candidates":[{"content":{"parts":[{"text":"check."}],"role":"model"}}]}

        data: {"candidates":[{"content":{"parts":[{"functionCall":{"name":"get_weather","args":{"city":"Paris"}}}],"role":"model"}}],"usageMetadata":{"promptTokenCount":12,"candidatesTokenCount":7,"totalTokenCount":19}}

        """;

    private const string AnthropicStream = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1","model":"claude-sonnet-4-5-20250929","usage":{"input_tokens":12,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Let me "}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"check."}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: content_block_start
        data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_1","name":"get_weather","input":{}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"city\":"}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"\"Paris\"}"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":1}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":7}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    /// <summary>A provider that implements only the pre-REQ-RAG-067 members.</summary>
    private sealed class LegacyToolProvider : ILlmProvider
    {
        public string Name => "Legacy";

        public string ModelName => "legacy-model";

        public bool SupportsToolCalling => true;

        public bool SupportsStreaming => true;

#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
        public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;
#pragma warning restore CS0067

        public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmResponse
            {
                Content = "Checking.",
                ToolCalls = [new ToolCall { Id = "c1", Name = "get_weather", ArgumentsJson = """{"city":"Paris"}""" }],
                Usage = new TokenUsage { InputTokens = 3, OutputTokens = 2 },
                FinishReason = "tool_calls"
            });

        public async IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return "unused";
        }

        public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class => throw new NotSupportedException();

        public int EstimateTokenCount(string text) => text.Length / 4;
    }
}
