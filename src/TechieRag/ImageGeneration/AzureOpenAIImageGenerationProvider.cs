using System.Diagnostics;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.ImageGeneration;

/// <summary>
/// Image generation through an Azure OpenAI resource,
/// <c>POST {endpoint}/openai/deployments/{deployment}/images/generations?api-version=…</c> (REQ-RAG-157 / BRD-215).
/// </summary>
/// <remarks>
/// <para>The same request and answer as OpenAI's Images API; only the URL, the <c>api-key</c> header and the
/// <c>api-version</c> differ. The model is the deployment, named in the URL, so the body carries none.</para>
/// <para>A deployment name does not say which model it serves, so the size is checked by the service, unless the
/// caller names the model through <see cref="ImageGenerationOptions.Model"/> (for example <c>gpt-image-1</c>), which
/// is then used only for the size check and the PNG request.</para>
/// </remarks>
public sealed class AzureOpenAIImageGenerationProvider : IImageGenerationProvider, IDisposable
{
    /// <summary>The API version the library sends when none is given.</summary>
    public const string DefaultApiVersion = "2025-04-01-preview";

    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string endpoint;
    private readonly string apiVersion;
    private readonly TimeSpan timeout;

    /// <summary>Initializes a new instance of the <see cref="AzureOpenAIImageGenerationProvider"/> class.</summary>
    /// <param name="apiKey">The resource key; held in memory only.</param>
    /// <param name="deployment">The image deployment's name.</param>
    /// <param name="endpoint">The resource endpoint, for example <c>https://my-resource.openai.azure.com</c>.</param>
    /// <param name="apiVersion">The API version; null for <see cref="DefaultApiVersion"/>.</param>
    /// <param name="requestTimeout">The default call timeout; null for 120 seconds.</param>
    /// <exception cref="ArgumentException">A key, deployment or endpoint is null or empty.</exception>
    public AzureOpenAIImageGenerationProvider(
        string apiKey, string deployment, string endpoint, string? apiVersion = null, TimeSpan? requestTimeout = null)
        : this(null, apiKey, deployment, endpoint, apiVersion, requestTimeout)
    {
    }

    /// <summary>Initializes a new instance sending through a given handler (tests).</summary>
    internal AzureOpenAIImageGenerationProvider(
        HttpMessageHandler? handler, string apiKey, string deployment, string endpoint, string? apiVersion, TimeSpan? requestTimeout)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(deployment);
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        this.apiKey = apiKey;
        ModelName = deployment;
        this.endpoint = endpoint;
        this.apiVersion = string.IsNullOrWhiteSpace(apiVersion) ? DefaultApiVersion : apiVersion.Trim();
        timeout = requestTimeout ?? ImageHttp.DefaultTimeout;
        httpClient = ImageHttp.CreateClient(handler);
    }

    /// <inheritdoc/>
    public string Name => ImageGenerationProviderFactory.AzureOpenAIName;

    /// <summary>Gets the deployment name.</summary>
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
        var model = options?.Model;
        ImageHttp.CheckArguments(prompt, size, model is null ? [] : OpenAIImageFormat.SizesFor(model), model ?? ModelName);

        var watch = Stopwatch.StartNew();
        var url = ImageHttp.Url(endpoint,
            $"openai/deployments/{Uri.EscapeDataString(ModelName)}/images/generations?api-version={Uri.EscapeDataString(apiVersion)}");
        var answer = await ImageHttp.PostJsonAsync(
            httpClient,
            url,
            OpenAIImageFormat.Body(null, prompt, size, options, model is null || OpenAIImageFormat.IsGptImage(model)),
            headers => headers.Add("api-key", apiKey),
            options?.Timeout ?? timeout,
            cancellationToken).ConfigureAwait(false);

        return OpenAIImageFormat.Result(answer, size, Name, model ?? ModelName, watch.Elapsed);
    }

    /// <inheritdoc/>
    public void Dispose() => httpClient.Dispose();
}
