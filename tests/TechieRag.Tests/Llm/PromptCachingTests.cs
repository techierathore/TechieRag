using System.Text.Json;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Wire-format tests for prompt-caching passthrough (REQ-RAG-043 / BRD-124).
/// </summary>
/// <remarks>
/// There is no live LLM provider on the build host, so these prove what TechieRag sends and how it
/// reads a recorded usage block back. Whether a provider's cache actually hits, and what it bills,
/// is outside anything the library can assert.
/// </remarks>
public class PromptCachingTests
{
    private static IReadOnlyList<ChatMessage> Conversation() =>
    [
        ChatMessage.System("You are a careful assistant."),
        ChatMessage.User("Hello")
    ];

    /// <summary>Caching the system prompt forces the block form and attaches the breakpoint.</summary>
    [Fact]
    public async Task AnthropicMarksTheSystemPrompt()
    {
        var handler = new CapturingHandler(AnthropicResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };
        var provider = new AnthropicLlmProvider(client, "claude-sonnet-4-5-20250929");

        await provider.ChatAsync(
            Conversation(),
            new LlmCompletionOptions { PromptCache = new PromptCacheOptions { CacheSystemPrompt = true } });

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var system = doc.RootElement.GetProperty("system");

        Assert.Equal(JsonValueKind.Array, system.ValueKind);
        Assert.Equal("ephemeral", system[0].GetProperty("cache_control").GetProperty("type").GetString());
    }

    /// <summary>Without cache options the system prompt keeps the cheaper plain-string shape.</summary>
    [Fact]
    public async Task AnthropicLeavesTheSystemPromptAloneWhenNotCaching()
    {
        var handler = new CapturingHandler(AnthropicResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };
        var provider = new AnthropicLlmProvider(client, "claude-sonnet-4-5-20250929");

        await provider.ChatAsync(Conversation());

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("system").ValueKind);
    }

    /// <summary>A long TTL selects the one-hour tier; the default tier stays unstated.</summary>
    [Fact]
    public async Task AnthropicSelectsTheOneHourTier()
    {
        var handler = new CapturingHandler(AnthropicResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };
        var provider = new AnthropicLlmProvider(client, "claude-sonnet-4-5-20250929");

        await provider.ChatAsync(
            Conversation(),
            new LlmCompletionOptions
            {
                PromptCache = new PromptCacheOptions { CacheSystemPrompt = true, Ttl = TimeSpan.FromHours(1) }
            });

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var control = doc.RootElement.GetProperty("system")[0].GetProperty("cache_control");

        Assert.Equal("1h", control.GetProperty("ttl").GetString());
    }

    /// <summary>A short TTL leaves the tier unstated so the extended-TTL beta is not required.</summary>
    [Fact]
    public async Task AnthropicOmitsTtlForTheDefaultTier()
    {
        var handler = new CapturingHandler(AnthropicResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };
        var provider = new AnthropicLlmProvider(client, "claude-sonnet-4-5-20250929");

        await provider.ChatAsync(
            Conversation(),
            new LlmCompletionOptions
            {
                PromptCache = new PromptCacheOptions { CacheSystemPrompt = true, Ttl = TimeSpan.FromMinutes(5) }
            });

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var control = doc.RootElement.GetProperty("system")[0].GetProperty("cache_control");

        Assert.False(control.TryGetProperty("ttl", out _));
    }

    /// <summary>
    /// The breakpoint lands on the LAST tool definition, because Anthropic caches the prefix up to
    /// the marker — marking the first would cache almost nothing.
    /// </summary>
    [Fact]
    public async Task AnthropicMarksTheFinalToolDefinition()
    {
        var handler = new CapturingHandler(AnthropicResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };
        var provider = new AnthropicLlmProvider(client, "claude-sonnet-4-5-20250929");

        var options = new LlmCompletionOptions
        {
            Tools =
            [
                new ToolDefinition { Name = "first", Description = "d", ParametersSchema = "{}" },
                new ToolDefinition { Name = "second", Description = "d", ParametersSchema = "{}" }
            ],
            PromptCache = new PromptCacheOptions { CacheToolDefinitions = true }
        };

        await provider.ChatAsync(Conversation(), options);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var tools = doc.RootElement.GetProperty("tools");

        Assert.False(tools[0].TryGetProperty("cache_control", out _));
        Assert.True(tools[1].TryGetProperty("cache_control", out _));
    }

    /// <summary>A message marked as the prefix boundary carries the breakpoint on its last block.</summary>
    [Fact]
    public async Task AnthropicHonoursAMessageCacheBoundary()
    {
        var handler = new CapturingHandler(AnthropicResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };
        var provider = new AnthropicLlmProvider(client, "claude-sonnet-4-5-20250929");

        var boundary = ChatMessage.User("A long retrieved context...");
        boundary.CacheBoundary = true;

        await provider.ChatAsync(
            [boundary, ChatMessage.User("And my question")],
            new LlmCompletionOptions { PromptCache = new PromptCacheOptions() });

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var messages = doc.RootElement.GetProperty("messages");

        Assert.True(messages[0].GetProperty("content")[0].TryGetProperty("cache_control", out _));
        Assert.Equal(JsonValueKind.String, messages[1].GetProperty("content").ValueKind);
    }

    /// <summary>
    /// The boundary flag is inert without cache options, so a message can carry it harmlessly through
    /// a provider or a call that is not caching.
    /// </summary>
    [Fact]
    public async Task AnthropicIgnoresACacheBoundaryWithoutCacheOptions()
    {
        var handler = new CapturingHandler(AnthropicResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };
        var provider = new AnthropicLlmProvider(client, "claude-sonnet-4-5-20250929");

        var boundary = ChatMessage.User("A long retrieved context...");
        boundary.CacheBoundary = true;

        await provider.ChatAsync([boundary]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.Equal(
            JsonValueKind.String,
            doc.RootElement.GetProperty("messages")[0].GetProperty("content").ValueKind);
    }

    /// <summary>Anthropic's cache accounting is surfaced separately from ordinary input tokens.</summary>
    [Fact]
    public async Task AnthropicReadsBackCacheTokens()
    {
        const string json = """
        {"id":"m1","model":"claude","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn",
         "usage":{"input_tokens":10,"output_tokens":4,"cache_creation_input_tokens":900,"cache_read_input_tokens":1200}}
        """;

        var handler = new CapturingHandler(json);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };
        var provider = new AnthropicLlmProvider(client, "claude-sonnet-4-5-20250929");

        var response = await provider.ChatAsync(Conversation());

        Assert.Equal(1200, response.Usage!.CacheReadTokens);
        Assert.Equal(900, response.Usage.CacheWriteTokens);
        Assert.Equal(10, response.Usage.InputTokens);
    }

    /// <summary>The OpenAI dialect can only express the routing key, and does.</summary>
    [Fact]
    public async Task OpenAiCompatibleSendsThePromptCacheKey()
    {
        var handler = new CapturingHandler(OpenAiResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test") };
        var provider = new OpenAICompatibleLlmProvider(client, "gpt-4o");

        await provider.ChatAsync(
            Conversation(),
            new LlmCompletionOptions { PromptCache = new PromptCacheOptions { CacheKey = "workspace-42" } });

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.Equal("workspace-42", doc.RootElement.GetProperty("prompt_cache_key").GetString());
    }

    /// <summary>
    /// Options the dialect cannot express are dropped rather than rejected, so one options object can
    /// travel across a fallback chain that spans providers.
    /// </summary>
    [Fact]
    public async Task OpenAiCompatibleDropsInexpressibleCacheOptions()
    {
        var handler = new CapturingHandler(OpenAiResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test") };
        var provider = new OpenAICompatibleLlmProvider(client, "gpt-4o");

        await provider.ChatAsync(
            Conversation(),
            new LlmCompletionOptions
            {
                PromptCache = new PromptCacheOptions { CacheSystemPrompt = true, Ttl = TimeSpan.FromHours(1) }
            });

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.False(doc.RootElement.TryGetProperty("prompt_cache_key", out _));
        Assert.False(doc.RootElement.TryGetProperty("cache_control", out _));
    }

    /// <summary>No cache options means no cache key on the wire.</summary>
    [Fact]
    public async Task OpenAiCompatibleSendsNoCacheKeyByDefault()
    {
        var handler = new CapturingHandler(OpenAiResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test") };
        var provider = new OpenAICompatibleLlmProvider(client, "gpt-4o");

        await provider.ChatAsync(Conversation());

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.False(doc.RootElement.TryGetProperty("prompt_cache_key", out _));
    }

    /// <summary>Automatically cached prompt tokens are reported when the service breaks them out.</summary>
    [Fact]
    public async Task OpenAiCompatibleReadsBackCachedTokens()
    {
        const string json = """
        {"id":"c1","model":"gpt-4o","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":1000,"completion_tokens":5,"total_tokens":1005,"prompt_tokens_details":{"cached_tokens":768}}}
        """;

        var handler = new CapturingHandler(json);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test") };
        var provider = new OpenAICompatibleLlmProvider(client, "gpt-4o");

        var response = await provider.ChatAsync(Conversation());

        Assert.Equal(768, response.Usage!.CacheReadTokens);
        Assert.Equal(1000, response.Usage.InputTokens);
    }

    /// <summary>A service that reports no cache breakdown yields zero, not a guess.</summary>
    [Fact]
    public async Task OpenAiCompatibleReportsZeroWhenNoBreakdownIsGiven()
    {
        var handler = new CapturingHandler(OpenAiResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test") };
        var provider = new OpenAICompatibleLlmProvider(client, "gpt-4o");

        var response = await provider.ChatAsync(Conversation());

        Assert.Equal(0, response.Usage!.CacheReadTokens);
    }

    /// <summary>Gemini's cache is a named out-of-band resource, passed straight through.</summary>
    [Fact]
    public async Task GeminiSendsTheCachedContentName()
    {
        var handler = new CapturingHandler(GeminiResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.test") };
        var provider = new GoogleGeminiLlmProvider(client, "key", "gemini-2.0-flash");

        await provider.ChatAsync(
            Conversation(),
            new LlmCompletionOptions
            {
                PromptCache = new PromptCacheOptions { ProviderCacheId = "cachedContents/abc123" }
            });

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.Equal("cachedContents/abc123", doc.RootElement.GetProperty("cachedContent").GetString());
    }

    /// <summary>Gemini reports how much of the prompt came from the cached resource.</summary>
    [Fact]
    public async Task GeminiReadsBackCachedContentTokens()
    {
        const string json = """
        {"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],
         "usageMetadata":{"promptTokenCount":900,"candidatesTokenCount":3,"totalTokenCount":903,"cachedContentTokenCount":850}}
        """;

        var handler = new CapturingHandler(json);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.test") };
        var provider = new GoogleGeminiLlmProvider(client, "key", "gemini-2.0-flash");

        var response = await provider.ChatAsync(Conversation());

        Assert.Equal(850, response.Usage!.CacheReadTokens);
    }

    /// <summary>Ollama has no cache-control wire format, so nothing is invented for it.</summary>
    [Fact]
    public async Task OllamaSendsNoCacheControls()
    {
        const string json = """
        {"model":"llama3","message":{"role":"assistant","content":"ok"},"done":true,"prompt_eval_count":5,"eval_count":2}
        """;

        var handler = new CapturingHandler(json);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaLlmProvider(client, "llama3");

        await provider.ChatAsync(
            Conversation(),
            new LlmCompletionOptions
            {
                PromptCache = new PromptCacheOptions { CacheSystemPrompt = true, CacheKey = "k" }
            });

        var body = handler.CapturedBody!;
        Assert.DoesNotContain("cache", body, StringComparison.OrdinalIgnoreCase);
    }

    private const string AnthropicResponseJson = """
    {"id":"m1","model":"claude","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":5,"output_tokens":2}}
    """;

    private const string OpenAiResponseJson = """
    {"id":"c1","model":"gpt-4o","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":2,"total_tokens":7}}
    """;

    private const string GeminiResponseJson = """
    {"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":2,"totalTokenCount":7}}
    """;
}
