using System.Text.Json;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Wire-format tests for image chat input across the built-in providers (REQ-RAG-039 / BRD-120).
/// </summary>
/// <remarks>
/// There is no live LLM provider on the build host. These prove that the image survives serialization
/// into each provider's documented shape; they cannot prove a model looked at it.
/// </remarks>
public class VisionRequestTests
{
    private const string ImageBase64 = "QUJD";

    private static ChatMessage VisionMessage() =>
        ChatMessage.UserWithImages("What is this?", ChatImage.FromBase64(ImageBase64, "image/png"));

    /// <summary>Anthropic takes a native image content block carrying the base64 source.</summary>
    [Fact]
    public async Task AnthropicSendsBase64ImageBlock()
    {
        var handler = new CapturingHandler(AnthropicResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };
        var provider = new AnthropicLlmProvider(client, "claude-sonnet-4-5-20250929");

        await provider.ChatAsync([VisionMessage()]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("image", content[1].GetProperty("type").GetString());

        var source = content[1].GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/png", source.GetProperty("media_type").GetString());
        Assert.Equal(ImageBase64, source.GetProperty("data").GetString());
    }

    /// <summary>Anthropic can also be handed a URL for the service to fetch.</summary>
    [Fact]
    public async Task AnthropicSendsUrlImageSource()
    {
        var handler = new CapturingHandler(AnthropicResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };
        var provider = new AnthropicLlmProvider(client, "claude-sonnet-4-5-20250929");

        var image = ChatImage.FromUrl(new Uri("https://example.com/cat.png"), "image/png");
        await provider.ChatAsync([ChatMessage.UserWithImages("Look", image)]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var source = doc.RootElement.GetProperty("messages")[0]
            .GetProperty("content")[1].GetProperty("source");

        Assert.Equal("url", source.GetProperty("type").GetString());
        Assert.Equal("https://example.com/cat.png", source.GetProperty("url").GetString());
    }

    /// <summary>A text-only message keeps the plain string shape rather than being widened to blocks.</summary>
    [Fact]
    public async Task AnthropicKeepsPlainStringForTextOnlyMessages()
    {
        var handler = new CapturingHandler(AnthropicResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };
        var provider = new AnthropicLlmProvider(client, "claude-sonnet-4-5-20250929");

        await provider.ChatAsync([ChatMessage.User("just text")]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        Assert.Equal(JsonValueKind.String, content.ValueKind);
        Assert.Equal("just text", content.GetString());
    }

    /// <summary>The OpenAI dialect wraps inline bytes in a data URI under image_url.</summary>
    [Fact]
    public async Task OpenAiCompatibleSendsImageUrlPart()
    {
        var handler = new CapturingHandler(OpenAiResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test") };
        var provider = new OpenAICompatibleLlmProvider(client, "gpt-4o");

        await provider.ChatAsync([VisionMessage()]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("image_url", content[1].GetProperty("type").GetString());
        Assert.Equal(
            $"data:image/png;base64,{ImageBase64}",
            content[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    /// <summary>LM Studio speaks the same dialect, so a local vision model gets the same shape.</summary>
    [Fact]
    public async Task LmStudioSendsImageUrlPart()
    {
        var handler = new CapturingHandler(OpenAiResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234") };
        var provider = new LmStudioLlmProvider(client, "qwen2-vl");

        await provider.ChatAsync([VisionMessage()]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        Assert.Equal("image_url", content[1].GetProperty("type").GetString());
    }

    /// <summary>Gemini takes inline bytes as an inlineData part with a mimeType.</summary>
    [Fact]
    public async Task GeminiSendsInlineDataPart()
    {
        var handler = new CapturingHandler(GeminiResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.test") };
        var provider = new GoogleGeminiLlmProvider(client, "key", "gemini-2.0-flash");

        await provider.ChatAsync([VisionMessage()]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var parts = doc.RootElement.GetProperty("contents")[0].GetProperty("parts");

        Assert.Equal("What is this?", parts[0].GetProperty("text").GetString());
        var inline = parts[1].GetProperty("inlineData");
        Assert.Equal("image/png", inline.GetProperty("mimeType").GetString());
        Assert.Equal(ImageBase64, inline.GetProperty("data").GetString());
    }

    /// <summary>A referenced image reaches Gemini as fileData rather than being inlined.</summary>
    [Fact]
    public async Task GeminiSendsFileDataForUrlImages()
    {
        var handler = new CapturingHandler(GeminiResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.test") };
        var provider = new GoogleGeminiLlmProvider(client, "key", "gemini-2.0-flash");

        var image = ChatImage.FromUrl(new Uri("https://example.com/cat.png"), "image/png");
        await provider.ChatAsync([ChatMessage.UserWithImages("Look", image)]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var fileData = doc.RootElement.GetProperty("contents")[0]
            .GetProperty("parts")[1].GetProperty("fileData");

        Assert.Equal("https://example.com/cat.png", fileData.GetProperty("fileUri").GetString());
    }

    /// <summary>Ollama keeps images in a sibling array of bare base64 strings.</summary>
    [Fact]
    public async Task OllamaSendsImagesArray()
    {
        var handler = new CapturingHandler(OllamaResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaLlmProvider(client, "llava");

        await provider.ChatAsync([VisionMessage()]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var message = doc.RootElement.GetProperty("messages")[0];

        Assert.Equal("What is this?", message.GetProperty("content").GetString());
        var images = message.GetProperty("images");
        Assert.Equal(JsonValueKind.Array, images.ValueKind);
        Assert.Equal(ImageBase64, images[0].GetString());
    }

    /// <summary>
    /// Ollama cannot fetch an image, so a URL-referenced one fails loudly instead of sending a
    /// question about a picture with no picture attached.
    /// </summary>
    [Fact]
    public async Task OllamaRefusesUrlImages()
    {
        var handler = new CapturingHandler(OllamaResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaLlmProvider(client, "llava");

        var image = ChatImage.FromUrl(new Uri("https://example.com/cat.png"), "image/png");

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.ChatAsync([ChatMessage.UserWithImages("Look", image)]));
    }

    /// <summary>A text-only Ollama message carries no images key at all.</summary>
    [Fact]
    public async Task OllamaOmitsImagesForTextOnlyMessages()
    {
        var handler = new CapturingHandler(OllamaResponseJson);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaLlmProvider(client, "llama3");

        await provider.ChatAsync([ChatMessage.User("hello")]);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.False(doc.RootElement.GetProperty("messages")[0].TryGetProperty("images", out _));
    }

    /// <summary>Every built-in provider advertises vision through the optional capability interface.</summary>
    [Fact]
    public void BuiltInProvidersAdvertiseVision()
    {
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:1") };

        ILlmProvider[] providers =
        [
            new AnthropicLlmProvider(client, "m"),
            new OpenAICompatibleLlmProvider(client, "m"),
            new LmStudioLlmProvider(client, "m"),
            new AzureAIFoundryLlmProvider(client, "m"),
            new GoogleGeminiLlmProvider(client, "k", "m"),
            new OllamaLlmProvider(client, "m")
        ];

        Assert.All(providers, provider => Assert.True(provider.SupportsVision()));
    }

    /// <summary>
    /// A provider written before the modality existed is reported as text-only rather than being
    /// assumed capable — the whole point of keeping the capability off <see cref="ILlmProvider"/>.
    /// </summary>
    [Fact]
    public void ProviderWithoutTheCapabilityInterfaceIsTextOnly()
    {
        var provider = new TextOnlyProvider();

        Assert.False(provider.SupportsVision());
        Assert.True(provider.SupportsInput(ChatContentKind.Text));
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

    private const string OllamaResponseJson = """
    {"model":"llava","message":{"role":"assistant","content":"ok"},"done":true,"prompt_eval_count":5,"eval_count":2}
    """;

    /// <summary>A minimal provider that predates <see cref="IMultimodalLlmProvider"/>.</summary>
    private sealed class TextOnlyProvider : ILlmProvider
    {
        public string Name => "Text Only";
        public string ModelName => "text-only";
        public bool SupportsToolCalling => false;
        public bool SupportsStreaming => false;

        public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted
        {
            add { }
            remove { }
        }

        public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class =>
            throw new NotSupportedException();

        public int EstimateTokenCount(string text) => text.Length;
    }
}
