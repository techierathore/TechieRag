using System.Text.Json.Nodes;
using TechieRag.Models;

namespace TechieRag.ImageGeneration;

/// <summary>
/// OpenAI's image request and answer shape, shared by the OpenAI, openai-compatible and Azure OpenAI routes
/// (REQ-RAG-155, REQ-RAG-156, REQ-RAG-157).
/// </summary>
internal static class OpenAIImageFormat
{
    /// <summary>Builds the request body.</summary>
    /// <param name="model">The model, or null to leave it out (Azure names the deployment in the URL).</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="size">The size.</param>
    /// <param name="options">The options, or null.</param>
    /// <param name="gptImage">True to ask for PNG through <c>output_format</c>; false for base64 through <c>response_format</c>.</param>
    /// <returns>The body.</returns>
    public static JsonObject Body(string? model, string prompt, ImageSize size, ImageGenerationOptions? options, bool gptImage)
    {
        var body = new JsonObject { ["prompt"] = prompt, ["size"] = size.ToString(), ["n"] = 1 };
        if (model is not null)
        {
            body["model"] = model;
        }

        if (gptImage)
        {
            body["output_format"] = "png";
        }
        else
        {
            body["response_format"] = "b64_json";
        }

        if (options?.Quality is { Length: > 0 } quality)
        {
            body["quality"] = quality;
        }

        return body;
    }

    /// <summary>Reads the image and usage from an answer.</summary>
    /// <param name="answer">The answer.</param>
    /// <param name="size">The asked size.</param>
    /// <param name="provider">The connector name for the usage.</param>
    /// <param name="model">The model for the usage.</param>
    /// <param name="elapsed">How long the call took.</param>
    /// <returns>The image.</returns>
    public static GeneratedImage Result(JsonNode answer, ImageSize size, string provider, string model, TimeSpan elapsed)
    {
        var bytes = ImageHttp.Decode(ImageHttp.First(answer["data"])?["b64_json"]?.GetValue<string>(), provider);
        var usage = answer["usage"];
        return ImageBytes.Result(bytes, "image/png", size, new ImageGenerationUsage
        {
            ProviderName = provider,
            ModelName = model,
            Images = 1,
            InputTokens = ImageHttp.Int(usage?["input_tokens"]),
            OutputTokens = ImageHttp.Int(usage?["output_tokens"]),
            TotalTokens = ImageHttp.Int(usage?["total_tokens"]),
            Duration = elapsed
        });
    }

    /// <summary>True for the GPT Image family, which answers in PNG through <c>output_format</c>.</summary>
    /// <param name="model">The model.</param>
    /// <returns>True for a GPT Image model.</returns>
    public static bool IsGptImage(string model) => model.StartsWith("gpt-image", StringComparison.OrdinalIgnoreCase);

    /// <summary>The sizes a known model makes; empty for a model this library does not know, which the service checks.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The sizes.</returns>
    public static IReadOnlyList<ImageSize> SizesFor(string model) =>
        model.StartsWith("gpt-image-1", StringComparison.OrdinalIgnoreCase)
            ? [new(1024, 1024), new(1536, 1024), new(1024, 1536)]
            : model.Equals("dall-e-3", StringComparison.OrdinalIgnoreCase)
                ? [new(1024, 1024), new(1792, 1024), new(1024, 1792)]
                : model.Equals("dall-e-2", StringComparison.OrdinalIgnoreCase)
                    ? [new(256, 256), new(512, 512), new(1024, 1024)]
                    : [];
}
