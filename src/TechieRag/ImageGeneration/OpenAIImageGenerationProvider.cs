using System.Diagnostics;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.ImageGeneration;

/// <summary>
/// Image generation through OpenAI's Images API, <c>POST /v1/images/generations</c> (REQ-RAG-155 / BRD-213), and
/// through any service that speaks the same format at an endpoint the app gives, the <c>openai-compatible</c>
/// route (REQ-RAG-156 / BRD-214).
/// </summary>
/// <remarks>
/// GPT Image models are asked for PNG (<c>output_format</c>) and answer with base64 data and a token usage; other
/// models are asked for base64 (<c>response_format</c>). The key goes in the <c>Authorization</c> header only.
/// </remarks>
public sealed class OpenAIImageGenerationProvider : IImageGenerationProvider, IDisposable
{
    /// <summary>The default model.</summary>
    public const string DefaultModel = "gpt-image-1";

    /// <summary>The default endpoint.</summary>
    public const string DefaultEndpoint = "https://api.openai.com/v1";

    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string endpoint;
    private readonly TimeSpan timeout;

    /// <summary>Initializes a new instance of the <see cref="OpenAIImageGenerationProvider"/> class.</summary>
    /// <param name="apiKey">The API key; held in memory only.</param>
    /// <param name="model">The default model.</param>
    /// <param name="endpoint">The base URL in place of OpenAI's, for an OpenAI-compatible service; null for <see cref="DefaultEndpoint"/>.</param>
    /// <param name="requestTimeout">The default call timeout; null for 120 seconds.</param>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> or <paramref name="model"/> is null or empty.</exception>
    public OpenAIImageGenerationProvider(string apiKey, string model = DefaultModel, string? endpoint = null, TimeSpan? requestTimeout = null)
        : this(null, apiKey, model, endpoint, requestTimeout, ImageGenerationProviderFactory.OpenAIName)
    {
    }

    /// <summary>Initializes a new instance with a handler (tests) and a connector name (<c>openai-compatible</c>).</summary>
    internal OpenAIImageGenerationProvider(
        HttpMessageHandler? handler, string apiKey, string model, string? endpoint, TimeSpan? requestTimeout, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);
        this.apiKey = apiKey;
        ModelName = model;
        Name = name;
        this.endpoint = endpoint ?? DefaultEndpoint;
        timeout = requestTimeout ?? ImageHttp.DefaultTimeout;
        httpClient = ImageHttp.CreateClient(handler);
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ImageSize> SupportedSizes => OpenAIImageFormat.SizesFor(ModelName);

    /// <inheritdoc/>
    public async Task<GeneratedImage> GenerateImageAsync(
        string prompt,
        ImageSize size,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var model = options?.Model ?? ModelName;
        ImageHttp.CheckArguments(prompt, size, OpenAIImageFormat.SizesFor(model), model);

        var watch = Stopwatch.StartNew();
        var answer = await ImageHttp.PostJsonAsync(
            httpClient,
            ImageHttp.Url(endpoint, "images/generations"),
            OpenAIImageFormat.Body(model, prompt, size, options, OpenAIImageFormat.IsGptImage(model)),
            headers => headers.Authorization = new("Bearer", apiKey),
            options?.Timeout ?? timeout,
            cancellationToken).ConfigureAwait(false);

        return OpenAIImageFormat.Result(answer, size, Name, model, watch.Elapsed);
    }

    /// <inheritdoc/>
    public void Dispose() => httpClient.Dispose();
}
