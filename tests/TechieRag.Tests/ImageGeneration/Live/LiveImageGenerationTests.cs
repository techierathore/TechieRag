using TechieRag.ImageGeneration;
using TechieRag.Models;
using TechieRag.Tests.VectorStores.Live;
using Xunit;

namespace TechieRag.Tests.ImageGeneration.Live;

/// <summary>
/// Image generation against the real services (REQ-RAG-155 / BRD-213). Each test reads its key from the
/// git-ignored <c>livetests.settings.json</c> beside the tests (<c>ImageGeneration:OpenAIKey</c>,
/// <c>GeminiKey</c>, <c>VeniceKey</c>) and skips with the reason when the key is absent. Each call is billed.
/// </summary>
[Trait("Category", LiveImageFactAttribute.CategoryName)]
public sealed class LiveImageGenerationTests
{
    /// <summary>OpenAI returns a 1024x1024 PNG and a token usage.</summary>
    [LiveImageFact(KeyName = "OpenAIKey", DisplayName = "REQ-RAG-155 LiveOpenAIReturnsAPngOfTheAskedSize")]
    public Task LiveOpenAIReturnsAPng() => GenerateAsync("openai/gpt-image-1", "OpenAIKey", expectTokens: true);

    /// <summary>Gemini returns a 1024x1024 PNG and a token usage.</summary>
    [LiveImageFact(KeyName = "GeminiKey", DisplayName = "REQ-RAG-155 LiveGeminiReturnsAPngOfTheAskedSize")]
    public Task LiveGeminiReturnsAPng() => GenerateAsync("gemini/gemini-2.5-flash-image", "GeminiKey", expectTokens: true);

    /// <summary>Venice returns a 1024x1024 PNG; it reports no token usage.</summary>
    [LiveImageFact(KeyName = "VeniceKey", DisplayName = "REQ-RAG-155 LiveVeniceReturnsAPngOfTheAskedSize")]
    public Task LiveVeniceReturnsAPng() => GenerateAsync("venice/venice-sd35", "VeniceKey", expectTokens: false);

    /// <summary>Generates one small image on a route and checks its type, size and usage.</summary>
    private static async Task GenerateAsync(string route, string keyName, bool expectTokens)
    {
        var provider = ImageGenerationProviderFactory.CreateForModel(route, LiveImageFactAttribute.Key(keyName)!);

        var image = await provider.GenerateImageAsync("a plain red circle on a white background", ImageSize.Square1024,
            new ImageGenerationOptions { Quality = route.StartsWith("openai", StringComparison.Ordinal) ? "low" : null });

        ImageGenerationTests.AssertPng(image, ImageSize.Square1024);
        Assert.Equal(1, image.Usage.Images);
        if (expectTokens)
        {
            Assert.True(image.Usage.TotalTokens > 0, "the service reported no token usage");
        }
    }
}
