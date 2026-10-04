using System.Text.Json;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Finish-reason (REQ-RAG-112 / TR-RAG-002) and context-window (REQ-RAG-113 / TR-RAG-003) tests for the Ollama provider, raised by Lekhak.
/// </summary>
/// <remarks>
/// Run against a stub answering in Ollama's documented <c>/api/chat</c> shape; there is no live
/// Ollama on the build host.
/// </remarks>
public class OllamaRequestOptionsTests
{
    private const string LengthReplyJson =
        """{"message":{"role":"assistant","content":"Hello"},"done":true,"done_reason":"length","prompt_eval_count":3,"eval_count":1}""";

    private const string StopReplyJson =
        """{"message":{"role":"assistant","content":"Hello"},"done":true,"done_reason":"stop","prompt_eval_count":3,"eval_count":1}""";

    private const string LengthStreamNdjson =
        """
        {"message":{"role":"assistant","content":"Hel"},"done":false}
        {"message":{"role":"assistant","content":"lo"},"done":false}
        {"message":{"role":"assistant","content":""},"done":true,"done_reason":"length","prompt_eval_count":3,"eval_count":2}
        """;

    private static OllamaLlmProvider CreateProvider(CapturingHandler handler, int? contextTokens = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") }, "llama3.2", contextTokens: contextTokens);

    /// <summary>A reply Ollama cut off at num_predict reports "length" from ChatAsync, not "stop".</summary>
    [Fact(DisplayName = "REQ-RAG-112 OllamaChatReportsLength")]
    public async Task OllamaChatReportsLength()
    {
        var provider = CreateProvider(new CapturingHandler(LengthReplyJson));

        var reply = await provider.ChatAsync([ChatMessage.User("hi")]);

        Assert.Equal("length", reply.FinishReason);
    }

    /// <summary>A reply that ended naturally still reports "stop".</summary>
    [Fact(DisplayName = "REQ-RAG-112 OllamaChatReportsStop")]
    public async Task OllamaChatReportsStop()
    {
        var provider = CreateProvider(new CapturingHandler(StopReplyJson));

        var reply = await provider.ChatAsync([ChatMessage.User("hi")]);

        Assert.Equal("stop", reply.FinishReason);
    }

    /// <summary>The streamed Completed event carries "length" from the final chunk's done_reason.</summary>
    [Fact(DisplayName = "REQ-RAG-112 OllamaStreamReportsLength")]
    public async Task OllamaStreamReportsLength()
    {
        var provider = CreateProvider(new CapturingHandler(LengthStreamNdjson));

        LlmStreamEvent? completed = null;
        await foreach (var streamEvent in provider.ChatStreamEventsAsync([ChatMessage.User("hi")]))
        {
            if (streamEvent.Kind == LlmStreamEventKind.Completed) completed = streamEvent;
        }

        Assert.NotNull(completed);
        Assert.Equal("length", completed.FinishReason);
    }

    /// <summary>A configured context size is sent as options.num_ctx.</summary>
    [Fact(DisplayName = "REQ-RAG-113 OllamaSendsNumCtx")]
    public async Task OllamaSendsNumCtx()
    {
        var handler = new CapturingHandler(StopReplyJson);
        var provider = CreateProvider(handler, contextTokens: 32768);

        await provider.ChatAsync([ChatMessage.User("hi")]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.Equal(32768, doc.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
    }

    /// <summary>With no context size configured, no num_ctx is sent and Ollama keeps its own default.</summary>
    [Fact(DisplayName = "REQ-RAG-113 OllamaOmitsNumCtxWhenUnset")]
    public async Task OllamaOmitsNumCtxWhenUnset()
    {
        var handler = new CapturingHandler(StopReplyJson);
        var provider = CreateProvider(handler);

        await provider.ChatAsync([ChatMessage.User("hi")]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.False(doc.RootElement.TryGetProperty("options", out _));
    }
}
