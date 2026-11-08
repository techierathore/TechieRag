using System.Diagnostics;
using System.Text.Json.Nodes;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.ImageGeneration;

/// <summary>
/// Image generation through Venice AI, <c>POST /api/v1/image/generate</c> (REQ-RAG-155 / BRD-213;
/// https://docs.venice.ai/api-reference/endpoint/image/generate, checked 2026-11-08).
/// </summary>
/// <remarks>
/// <para>Asks for PNG with base64 in JSON (<c>format: png</c>, <c>return_binary: false</c>) at the given width and
/// height, which pixel-based models such as <c>venice-sd35</c> honour (1 to 1280 a side). Models that take an
/// aspect ratio or a resolution tier instead refuse a width and height; name another model per call.</para>
/// <para>Venice reports no usage in its answer: the usage carries the image count and no token counts. The key goes
/// in the <c>Authorization</c> header only.</para>
/// </remarks>
public sealed class VeniceImageGenerationProvider : IImageGenerationProvider, IDisposable
{
    /// <summary>The default model, a pixel-sized one.</summary>
    public const string DefaultModel = "venice-sd35";

    /// <summary>The default endpoint.</summary>
    public const string DefaultEndpoint = "https://api.venice.ai/api/v1";

    /// <summary>The largest side Venice accepts, in pixels.</summary>
    public const int MaxSide = 1280;

    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string endpoint;
    private readonly TimeSpan timeout;

    /// <summary>Initializes a new instance of the <see cref="VeniceImageGenerationProvider"/> class.</summary>
    /// <param name="apiKey">The API key; held in memory only.</param>
    /// <param name="model">The default model.</param>
    /// <param name="endpoint">The base URL in place of Venice's; null for <see cref="DefaultEndpoint"/>.</param>
    /// <param name="requestTimeout">The default call timeout; null for 120 seconds.</param>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> or <paramref name="model"/> is null or empty.</exception>
    public VeniceImageGenerationProvider(string apiKey, string model = DefaultModel, string? endpoint = null, TimeSpan? requestTimeout = null)
        : this(null, apiKey, model, endpoint, requestTimeout)
    {
    }

    /// <summary>Initializes a new instance sending through a given handler (tests).</summary>
    internal VeniceImageGenerationProvider(HttpMessageHandler? handler, string apiKey, string model, string? endpoint, TimeSpan? requestTimeout)
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
    public string Name => "venice";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ImageSize> SupportedSizes => [];

    /// <inheritdoc/>
    public async Task<GeneratedImage> GenerateImageAsync(
        string prompt,
        ImageSize size,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var model = options?.Model ?? ModelName;
        ImageHttp.CheckArguments(prompt, size, [], model);
        if (size.Width > MaxSide || size.Height > MaxSide)
        {
            throw new ArgumentException($"Venice makes images up to {MaxSide} pixels a side; {size} is larger.", nameof(size));
        }

        var body = new JsonObject
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["width"] = size.Width,
            ["height"] = size.Height,
            ["format"] = "png",
            ["return_binary"] = false
        };
        if (options?.NegativePrompt is { Length: > 0 } negative)
        {
            body["negative_prompt"] = negative;
        }

        if (options?.Seed is { } seed)
        {
            body["seed"] = seed;
        }

        if (options?.Quality is { Length: > 0 } quality)
        {
            body["quality"] = quality;
        }

        var watch = Stopwatch.StartNew();
        var answer = await ImageHttp.PostJsonAsync(
            httpClient,
            ImageHttp.Url(endpoint, "image/generate"),
            body,
            headers => headers.Authorization = new("Bearer", apiKey),
            options?.Timeout ?? timeout,
            cancellationToken).ConfigureAwait(false);

        var images = answer["images"] as JsonArray;
        var bytes = ImageHttp.Decode(images?.FirstOrDefault()?.GetValue<string>(), "Venice");
        return ImageBytes.Result(bytes, "image/png", size, new ImageGenerationUsage
        {
            ProviderName = Name,
            ModelName = model,
            Images = images?.Count ?? 1,
            Duration = watch.Elapsed
        });
    }

    /// <inheritdoc/>
    public void Dispose() => httpClient.Dispose();
}
