using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Thinking text from reasoning models on the OpenAI dialect (REQ-RAG-137 / Chatur TR-RAG-008, and
/// REQ-RAG-138 / Chatur TR-RAG-009).
/// </summary>
/// <remarks>
/// The streams are the shapes Chatur recorded against OpenCode Go on TechieRag 1.1.7: kimi-k2.7-code sends
/// <c>reasoning_content</c> fragments before the answer; minimax-m3 opens the answer text with a
/// <c>&lt;think&gt;</c> block. Both run through the three providers that share the OpenAI stream reader.
/// </remarks>
public class ThinkingStreamTests
{
    /// <summary>The providers that speak the OpenAI chat-completions dialect.</summary>
    public static TheoryData<string> OpenAIDialectProviders => new() { "OpenAICompatible", "LmStudio", "AzureAIFoundry" };

    private static readonly IReadOnlyList<ChatMessage> Conversation = [ChatMessage.User("say hello")];

    private const string ReasoningContentStream = """
        data: {"choices":[{"delta":{"role":"assistant","reasoning_content":"The user wants "}}]}

        data: {"choices":[{"delta":{"reasoning_content":"a greeting."}}]}

        data: {"choices":[{"delta":{"content":"Hello"}}]}

        data: {"choices":[{"delta":{"content":"!"},"finish_reason":"stop"}]}

        data: {"choices":[],"usage":{"prompt_tokens":9,"completion_tokens":33,"total_tokens":42,"completion_tokens_details":{"reasoning_tokens":22}}}

        data: [DONE]

        """;

    private const string ThinkTagStream = """
        data: {"choices":[{"delta":{"role":"assistant","content":"<thi"}}]}

        data: {"choices":[{"delta":{"content":"nk>The user greets"}}]}

        data: {"choices":[{"delta":{"content":" me.</th"}}]}

        data: {"choices":[{"delta":{"content":"ink>\n\nHello"}}]}

        data: {"choices":[{"delta":{"content":" there!"},"finish_reason":"stop"}]}

        data: [DONE]

        """;

    /// <summary>
    /// A stream with <c>reasoning_content</c> fragments yields them as thinking events before the answer,
    /// the answer text holds only the answer, and the completed event still carries the reasoning count.
    /// </summary>
    /// <param name="provider">The provider under test.</param>
    [Theory(DisplayName = "REQ-RAG-137 ReasoningContentStreamsAsThinking")]
    [MemberData(nameof(OpenAIDialectProviders))]
    public async Task ReasoningContentStreamsAsThinking(string provider)
    {
        var events = await CollectAsync(TypedStreamingTests.Create(provider, new CapturingHandler(ReasoningContentStream)));

        Assert.Equal(
            [LlmStreamEventKind.Thinking, LlmStreamEventKind.Thinking, LlmStreamEventKind.TextDelta, LlmStreamEventKind.TextDelta, LlmStreamEventKind.Completed],
            events.Select(e => e.Kind));
        Assert.Equal("The user wants a greeting.", Concat(events, LlmStreamEventKind.Thinking));
        Assert.Equal("Hello!", Concat(events, LlmStreamEventKind.TextDelta));
        Assert.Equal(22, events[^1].Usage!.ReasoningTokens);
    }

    /// <summary>The text-only stream never carries thinking: <c>ChatStreamAsync</c> yields the answer alone.</summary>
    [Fact(DisplayName = "REQ-RAG-137 TextStreamLeavesThinkingOut")]
    public async Task TextStreamLeavesThinkingOut()
    {
        var llm = TypedStreamingTests.Create("OpenAICompatible", new CapturingHandler(ReasoningContentStream));

        var tokens = new List<string>();
        await foreach (var token in llm.ChatStreamAsync(Conversation)) tokens.Add(token);

        Assert.Equal("Hello!", string.Concat(tokens));
    }

    /// <summary>A whole response returns <c>reasoning_content</c> in <see cref="LlmResponse.Thinking"/>, not in the answer.</summary>
    /// <param name="provider">The provider under test.</param>
    [Theory(DisplayName = "REQ-RAG-137 ChatReturnsReasoningAsThinking")]
    [MemberData(nameof(OpenAIDialectProviders))]
    public async Task ChatReturnsReasoningAsThinking(string provider)
    {
        const string body = """
            {"model":"kimi-k2.7-code","choices":[{"message":{"role":"assistant","content":"Hello!","reasoning_content":"The user wants a greeting."},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":9,"completion_tokens":33,"total_tokens":42,"completion_tokens_details":{"reasoning_tokens":22}}}
            """;
        var llm = TypedStreamingTests.Create(provider, new CapturingHandler(body));

        var response = await llm.ChatAsync(Conversation);

        Assert.Equal("Hello!", response.Content);
        Assert.Equal("The user wants a greeting.", response.Thinking);
        Assert.Equal(22, response.Usage.ReasoningTokens);
    }

    /// <summary>
    /// A <c>&lt;think&gt;</c> block opening the answer, with both tags cut across chunks, streams as thinking,
    /// and the answer starts at its first word.
    /// </summary>
    /// <param name="provider">The provider under test.</param>
    [Theory(DisplayName = "REQ-RAG-138 LeadingThinkBlockStreamsAsThinking")]
    [MemberData(nameof(OpenAIDialectProviders))]
    public async Task LeadingThinkBlockStreamsAsThinking(string provider)
    {
        var events = await CollectAsync(TypedStreamingTests.Create(provider, new CapturingHandler(ThinkTagStream)));

        Assert.Equal("The user greets me.", Concat(events, LlmStreamEventKind.Thinking));
        Assert.Equal("Hello there!", Concat(events, LlmStreamEventKind.TextDelta));
        Assert.DoesNotContain(events, e => e.Kind == LlmStreamEventKind.TextDelta && e.Text!.Contains("think", StringComparison.Ordinal));
        Assert.Equal(LlmStreamEventKind.Completed, events[^1].Kind);
    }

    /// <summary>A whole response that opens with a <c>&lt;think&gt;</c> block returns it in <see cref="LlmResponse.Thinking"/>.</summary>
    [Fact(DisplayName = "REQ-RAG-138 ChatSplitsLeadingThinkBlock")]
    public async Task ChatSplitsLeadingThinkBlock()
    {
        const string body = """
            {"model":"minimax-m3","choices":[{"message":{"role":"assistant","content":"<think>\nThe user greets me.\n</think>\n\nHello there!"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":9,"completion_tokens":20,"total_tokens":29}}
            """;
        var llm = TypedStreamingTests.Create("OpenAICompatible", new CapturingHandler(body));

        var response = await llm.ChatAsync(Conversation);

        Assert.Equal("Hello there!", response.Content);
        Assert.Equal("\nThe user greets me.\n", response.Thinking);
    }

    /// <summary>
    /// Only a block that opens the answer is thinking: a <c>&lt;think&gt;</c> later in the text, an answer
    /// that merely starts with "&lt;", and an answer with no tag at all are returned unchanged.
    /// </summary>
    /// <param name="content">The answer text.</param>
    [Theory(DisplayName = "REQ-RAG-138 ThinkTagLaterInAnswerIsKept")]
    [InlineData("Use <think>...</think> tags to wrap reasoning.")]
    [InlineData("<b>bold</b> text")]
    [InlineData("<thi")]
    [InlineData("Hello there!")]
    public void ThinkTagLaterInAnswerIsKept(string content)
    {
        var (thinking, answer) = ThinkBlockSplitter.Split(content);

        Assert.Null(thinking);
        Assert.Equal(content, answer);
    }

    /// <summary>A block the model never closed (the token limit hit mid-thought) is all thinking, with no answer.</summary>
    [Fact(DisplayName = "REQ-RAG-138 UnclosedThinkBlockIsAllThinking")]
    public void UnclosedThinkBlockIsAllThinking()
    {
        var splitter = new ThinkBlockSplitter();

        var parts = splitter.Push("  <think>Still weighing it </").Concat(splitter.Flush()).ToList();

        Assert.All(parts, p => Assert.True(p.IsThinking));
        Assert.Equal("Still weighing it </", string.Concat(parts.Select(p => p.Text)));
    }

    private static async Task<List<LlmStreamEvent>> CollectAsync(ILlmProvider llm)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var streamEvent in llm.ChatStreamEventsAsync(Conversation)) events.Add(streamEvent);
        return events;
    }

    private static string Concat(IEnumerable<LlmStreamEvent> events, LlmStreamEventKind kind) =>
        string.Concat(events.Where(e => e.Kind == kind).Select(e => e.Text));
}
