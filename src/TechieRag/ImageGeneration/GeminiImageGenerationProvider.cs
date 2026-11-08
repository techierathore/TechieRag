using System.Diagnostics;
using System.Text.Json.Nodes;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.ImageGeneration;

/// <summary>
/// Image generation through Gemini's native image models, <c>POST /v1beta/models/{model}:generateContent</c> with
/// image output (REQ-RAG-155 / BRD-213).
/// </summary>
/// <remarks>
/// <para>Gemini sizes an image by aspect ratio and resolution tier, not by pixels: the asked size picks the ratio
/// (1:1, 2:3, 3:2, 3:4, 4:3, 4:5, 5:4, 9:16, 16:9 or 21:9, matched within 2 percent) and its long side picks the tier
/// (up to 1024 is 1K, up to 2048 is 2K, else 4K). 1024x1024 comes back exactly; other ratios come back at the
/// model's own pixel size for that ratio, which <see cref="GeneratedImage.Size"/> reports from the image header.</para>
/// <para>The usage is the answer's <c>usageMetadata</c>. The key goes in the <c>x-goog-api-key</c> header, never in
/// the URL.</para>
/// </remarks>
public sealed class GeminiImageGenerationProvider : IImageGenerationProvider, IDisposable
{
    /// <summary>The default model.</summary>
    public const string DefaultModel = "gemini-2.5-flash-image";

    /// <summary>The default endpoint.</summary>
    public const string DefaultEndpoint = "https://generativelanguage.googleapis.com";

    private static readonly (string Ratio, double Value)[] Ratios =
    [
        ("1:1", 1.0), ("2:3", 2.0 / 3), ("3:2", 3.0 / 2), ("3:4", 3.0 / 4), ("4:3", 4.0 / 3),
        ("4:5", 4.0 / 5), ("5:4", 5.0 / 4), ("9:16", 9.0 / 16), ("16:9", 16.0 / 9), ("21:9", 21.0 / 9)
    ];

    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string endpoint;
    private readonly TimeSpan timeout;

    /// <summary>Initializes a new instance of the <see cref="GeminiImageGenerationProvider"/> class.</summary>
    /// <param name="apiKey">The API key; held in memory only.</param>
    /// <param name="model">The default model.</param>
    /// <param name="endpoint">The base URL in place of Google's; null for <see cref="DefaultEndpoint"/>.</param>
    /// <param name="requestTimeout">The default call timeout; null for 120 seconds.</param>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> or <paramref name="model"/> is null or empty.</exception>
    public GeminiImageGenerationProvider(string apiKey, string model = DefaultModel, string? endpoint = null, TimeSpan? requestTimeout = null)
        : this(null, apiKey, model, endpoint, requestTimeout)
    {
    }

    /// <summary>Initializes a new instance sending through a given handler (tests).</summary>
    internal GeminiImageGenerationProvider(HttpMessageHandler? handler, string apiKey, string model, string? endpoint, TimeSpan? requestTimeout)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);
        this.apiKey = apiKey;
        ModelName = model;
        this.endpoint = endpoint ?? DefaultEndpoint;
        timeout = requestTimeout ?? ImageHttp.DefaultTimeout;
        httpClient = ImageHttp.CreateClient(handler);
    }

    /// <inheritdoc/>
    public string Name => "gemini";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <summary>Gets the size Gemini makes exactly; other sizes are accepted by aspect ratio (see the remarks).</summary>
    public IReadOnlyList<ImageSize> SupportedSizes => [ImageSize.Square1024];

    /// <inheritdoc/>
    public async Task<GeneratedImage> GenerateImageAsync(
        string prompt,
        ImageSize size,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var model = options?.Model ?? ModelName;
        ImageHttp.CheckArguments(prompt, size, [], model);
        var ratio = RatioOf(size)
            ?? throw new ArgumentException(
                $"Gemini makes images in the aspect ratios {string.Join(", ", Ratios.Select(r => r.Ratio))}; {size} is none of them.", nameof(size));
        var longSide = Math.Max(size.Width, size.Height);
        var tier = longSide <= 1024 ? "1K" : longSide <= 2048 ? "2K" : "4K";

        var imageConfig = new JsonObject { ["aspectRatio"] = ratio };
        if (tier != "1K")
        {
            imageConfig["imageSize"] = tier;
        }

        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt })
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray("IMAGE"),
                ["imageConfig"] = imageConfig
            }
        };

        var watch = Stopwatch.StartNew();
        var answer = await ImageHttp.PostJsonAsync(
            httpClient,
            ImageHttp.Url(endpoint, $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent"),
            body,
            headers => headers.Add("x-goog-api-key", apiKey),
            options?.Timeout ?? timeout,
            cancellationToken).ConfigureAwait(false);

        var inline = (ImageHttp.First(answer["candidates"])?["content"]?["parts"] as JsonArray)?
            .Select(part => part?["inlineData"] ?? part?["inline_data"])
            .FirstOrDefault(data => data?["data"] is not null);
        var bytes = ImageHttp.Decode(inline?["data"]?.GetValue<string>(), "Gemini");
        var stated = (inline?["mimeType"] ?? inline?["mime_type"])?.GetValue<string>();
        var usage = answer["usageMetadata"];
        return ImageBytes.Result(bytes, stated, size, new ImageGenerationUsage
        {
            ProviderName = Name,
            ModelName = model,
            Images = 1,
            InputTokens = ImageHttp.Int(usage?["promptTokenCount"]),
            OutputTokens = ImageHttp.Int(usage?["candidatesTokenCount"]),
            TotalTokens = ImageHttp.Int(usage?["totalTokenCount"]),
            Duration = watch.Elapsed
        });
    }

    /// <inheritdoc/>
    public void Dispose() => httpClient.Dispose();

    /// <summary>The supported aspect ratio of a size, within 2 percent, or null.</summary>
    private static string? RatioOf(ImageSize size)
    {
        var value = (double)size.Width / size.Height;
        foreach (var (ratio, target) in Ratios)
        {
            if (Math.Abs(value - target) / target <= 0.02)
            {
                return ratio;
            }
        }

        return null;
    }
}
