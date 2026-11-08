using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TechieRag.ImageGeneration;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.ImageGeneration;

/// <summary>
/// Image generation for OpenAI, Gemini and Venice (REQ-RAG-155 / BRD-213), against a local stand-in that answers
/// in each service's documented shape with a real PNG of the asked size. The live half, against the services
/// themselves, is <see cref="Live.LiveImageGenerationTests"/>.
/// </summary>
public sealed class ImageGenerationTests
{
    private const string Key = "sk-test-secret-key-0123456789";

    /// <summary>
    /// A keyed OpenAI provider, a prompt and a size give back PNG bytes of that size and the token usage, and the
    /// request asked for PNG at that size with the key only in the Authorization header.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-155 OpenAIReturnsPngBytesOfTheAskedSizeAndTheUsage")]
    public async Task OpenAIReturnsPngOfTheAskedSize()
    {
        var size = new ImageSize(1536, 1024);
        var handler = new StubHandler(_ => Json(new JsonObject
        {
            ["created"] = 1,
            ["data"] = new JsonArray(new JsonObject { ["b64_json"] = Convert.ToBase64String(Png(size)) }),
            ["usage"] = new JsonObject { ["input_tokens"] = 12, ["output_tokens"] = 4160, ["total_tokens"] = 4172 }
        }));
        var provider = ImageGenerationProviderFactory.Create(handler, "openai", Key, null, null, null);

        var image = await provider.GenerateImageAsync("a lighthouse at dawn", size);

        AssertPng(image, size);
        Assert.Equal((12, 4160, 4172), (image.Usage.InputTokens, image.Usage.OutputTokens, image.Usage.TotalTokens));
        Assert.Equal(("openai", "gpt-image-1", 1), (image.Usage.ProviderName, image.Usage.ModelName, image.Usage.Images));
        Assert.Equal("https://api.openai.com/v1/images/generations", handler.Url);
        Assert.Equal("1536x1024", handler.Body!["size"]!.GetValue<string>());
        Assert.Equal("png", handler.Body["output_format"]!.GetValue<string>());
        Assert.Equal($"Bearer {Key}", handler.Authorization);
        Assert.DoesNotContain(Key, handler.Url!, StringComparison.Ordinal);
    }

    /// <summary>A keyed Gemini provider gives back PNG bytes of 1024x1024 and the usage from <c>usageMetadata</c>.</summary>
    [Fact(DisplayName = "REQ-RAG-155 GeminiReturnsPngBytesOfTheAskedSizeAndTheUsage")]
    public async Task GeminiReturnsPngOfTheAskedSize()
    {
        var size = ImageSize.Square1024;
        var handler = new StubHandler(_ => Json(new JsonObject
        {
            ["candidates"] = new JsonArray(new JsonObject
            {
                ["content"] = new JsonObject
                {
                    ["parts"] = new JsonArray(
                        new JsonObject { ["text"] = "Here is your image." },
                        new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = "image/png", ["data"] = Convert.ToBase64String(Png(size)) } })
                }
            }),
            ["usageMetadata"] = new JsonObject { ["promptTokenCount"] = 9, ["candidatesTokenCount"] = 1290, ["totalTokenCount"] = 1299 }
        }));
        var provider = ImageGenerationProviderFactory.Create(handler, "gemini", Key, null, null, null);

        var image = await provider.GenerateImageAsync("a lighthouse at dawn", size);

        AssertPng(image, size);
        Assert.Equal((9, 1290, 1299), (image.Usage.InputTokens, image.Usage.OutputTokens, image.Usage.TotalTokens));
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-image:generateContent", handler.Url);
        Assert.Equal("1:1", handler.Body!["generationConfig"]!["imageConfig"]!["aspectRatio"]!.GetValue<string>());
        Assert.Equal("IMAGE", handler.Body["generationConfig"]!["responseModalities"]![0]!.GetValue<string>());
        Assert.Equal(Key, handler.GoogleKey);
        Assert.Null(handler.Authorization);
        Assert.DoesNotContain(Key, handler.Url!, StringComparison.Ordinal);
    }

    /// <summary>A keyed Venice provider gives back PNG bytes of the asked width and height, with the image count as usage.</summary>
    [Fact(DisplayName = "REQ-RAG-155 VeniceReturnsPngBytesOfTheAskedSizeAndTheUsage")]
    public async Task VeniceReturnsPngOfTheAskedSize()
    {
        var size = new ImageSize(768, 512);
        var handler = new StubHandler(_ => Json(new JsonObject
        {
            ["id"] = "generate-image-1",
            ["images"] = new JsonArray(Convert.ToBase64String(Png(size))),
            ["timing"] = new JsonObject { ["total"] = 1800 }
        }));
        var provider = ImageGenerationProviderFactory.Create(handler, "venice", Key, null, null, null);

        var image = await provider.GenerateImageAsync("a lighthouse at dawn", size, new ImageGenerationOptions { Seed = 42, NegativePrompt = "text" });

        AssertPng(image, size);
        Assert.Equal(("venice", "venice-sd35", 1), (image.Usage.ProviderName, image.Usage.ModelName, image.Usage.Images));
        Assert.Null(image.Usage.InputTokens);
        Assert.Equal("https://api.venice.ai/api/v1/image/generate", handler.Url);
        Assert.Equal((768, 512), (handler.Body!["width"]!.GetValue<int>(), handler.Body["height"]!.GetValue<int>()));
        Assert.Equal("png", handler.Body["format"]!.GetValue<string>());
        Assert.False(handler.Body["return_binary"]!.GetValue<bool>());
        Assert.Equal(42, handler.Body["seed"]!.GetValue<int>());
        Assert.Equal($"Bearer {Key}", handler.Authorization);
    }

    /// <summary>Routes resolve like the chat providers': <c>connector/model</c>, or a bare model that names one service.</summary>
    [Fact(DisplayName = "REQ-RAG-155 ImageRoutesResolveLikeChatRoutes")]
    public void ImageRoutesResolveLikeChatRoutes()
    {
        Assert.Equal(("openai", "gpt-image-1"), Describe(ImageGenerationProviderFactory.CreateForModel("openai/gpt-image-1", Key)));
        Assert.Equal(("openai", "dall-e-3"), Describe(ImageGenerationProviderFactory.CreateForModel("dall-e-3", Key)));
        Assert.Equal(("gemini", "gemini-3-pro-image"), Describe(ImageGenerationProviderFactory.CreateForModel("gemini/gemini-3-pro-image", Key)));
        Assert.Equal(("gemini", "gemini-2.5-flash-image"), Describe(ImageGenerationProviderFactory.CreateForModel("gemini-2.5-flash-image", Key)));
        Assert.Equal(("venice", "qwen-image"), Describe(ImageGenerationProviderFactory.CreateForModel("venice/qwen-image", Key)));
        Assert.Equal(("venice", "venice-sd35"), Describe(ImageGenerationProviderFactory.CreateForModel("venice", Key)));

        Assert.Throws<InvalidOperationException>(() => ImageGenerationProviderFactory.CreateForModel("anthropic/claude", Key));
        Assert.Throws<InvalidOperationException>(() => ImageGenerationProviderFactory.CreateForModel("flux-dev", Key));
        Assert.Throws<ArgumentException>(() => ImageGenerationProviderFactory.CreateForModel("openai/gpt-image-1", string.Empty));
        Assert.Throws<ArgumentException>(() => ImageGenerationProviderFactory.CreateForModel("openai/gpt-image-1", Key, endpoint: "ftp://x"));
    }

    /// <summary>A size the model does not make is refused before any request, naming the sizes it does make.</summary>
    [Fact]
    public async Task UnsupportedSizesAreRefusedBeforeTheCall()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("no request expected"));

        var openAi = await Assert.ThrowsAsync<ArgumentException>(() =>
            ImageGenerationProviderFactory.Create(handler, "openai", Key, null, null, null).GenerateImageAsync("x", new ImageSize(800, 600)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ImageGenerationProviderFactory.Create(handler, "venice", Key, null, null, null).GenerateImageAsync("x", new ImageSize(2048, 2048)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ImageGenerationProviderFactory.Create(handler, "gemini", Key, null, null, null).GenerateImageAsync("x", new ImageSize(1000, 100)));

        Assert.Contains("1024x1024, 1536x1024, 1024x1536", openAi.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
    }

    /// <summary>
    /// The key is never persisted or echoed: a refusal raises the library's HTTP error, and neither its message nor
    /// anything the provider exposes carries the key.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-155 TheKeyIsNeverEchoedOrPersisted")]
    public async Task TheKeyIsNeverEchoed()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"message":"Incorrect API key provided."}}""", Encoding.UTF8, "application/json")
        });
        var provider = ImageGenerationProviderFactory.Create(handler, "gemini", Key, null, null, null);

        var error = await Assert.ThrowsAsync<LlmHttpException>(() => provider.GenerateImageAsync("x", ImageSize.Square1024));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.DoesNotContain(Key, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, provider.ToString()!, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, handler.Url!, StringComparison.Ordinal);
    }

    /// <summary>An answer with no image is an error, never an empty image.</summary>
    [Fact]
    public async Task AnAnswerWithoutAnImageIsAnError()
    {
        var handler = new StubHandler(_ => Json(new JsonObject { ["data"] = new JsonArray() }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ImageGenerationProviderFactory.Create(handler, "openai", Key, null, null, null).GenerateImageAsync("x", ImageSize.Square1024));
    }

    /// <summary>Asserts an image is a PNG of a size.</summary>
    /// <param name="image">The image.</param>
    /// <param name="size">The size.</param>
    internal static void AssertPng(GeneratedImage image, ImageSize size)
    {
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(size, image.Size);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, image.Bytes[..4]);
    }

    /// <summary>Encodes a real, decodable PNG of a size: grey, 8-bit RGB.</summary>
    /// <param name="size">The size.</param>
    /// <returns>The PNG bytes.</returns>
    internal static byte[] Png(ImageSize size)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), size.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), size.Height);
        header[8] = 8;
        header[9] = 2;
        Chunk(output, "IHDR", header);

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + (size.Width * 3)];
            Array.Fill(row, (byte)0x80, 1, row.Length - 1);
            for (var y = 0; y < size.Height; y++)
            {
                zlib.Write(row);
            }
        }

        Chunk(output, "IDAT", raw.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>Writes one PNG chunk with its CRC.</summary>
    private static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        output.Write(typed);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        output.Write(crc);
    }

    /// <summary>The PNG CRC-32.</summary>
    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>A provider's connector and model.</summary>
    private static (string, string) Describe(TechieRag.Abstractions.IImageGenerationProvider provider) => (provider.Name, provider.ModelName);

    /// <summary>A JSON answer.</summary>
    private static HttpResponseMessage Json(JsonNode body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    /// <summary>Records the request and answers with a canned response.</summary>
    /// <param name="respond">Builds the response.</param>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <summary>Gets how many requests arrived.</summary>
        public int Calls { get; private set; }

        /// <summary>Gets the last request URL.</summary>
        public string? Url { get; private set; }

        /// <summary>Gets the last request body.</summary>
        public JsonNode? Body { get; private set; }

        /// <summary>Gets the last Authorization header.</summary>
        public string? Authorization { get; private set; }

        /// <summary>Gets the last x-goog-api-key header.</summary>
        public string? GoogleKey { get; private set; }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Url = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            GoogleKey = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.Single() : null;
            Body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }
}
