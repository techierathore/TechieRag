using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Generates images from a prompt (REQ-RAG-155 / BRD-213; re-scopes BRD-161 for images only).
/// </summary>
/// <remarks>
/// <para>Shipped for OpenAI, Gemini and Venice on raw <c>HttpClient</c> (ADR-003). Create one through
/// <see cref="ImageGeneration.ImageGenerationProviderFactory.CreateForModel"/> with a <c>connector/model</c> route, the way
/// chat providers are created, for example <c>openai/gpt-image-1</c>, <c>gemini/gemini-2.5-flash-image</c> or
/// <c>venice/venice-sd35</c>.</para>
/// <para>The API key is held in memory for the provider's lifetime only: never written to disk, a log, a
/// message or a URL.</para>
/// </remarks>
public interface IImageGenerationProvider
{
    /// <summary>Gets the connector name, for example <c>openai</c>.</summary>
    string Name { get; }

    /// <summary>Gets the default model.</summary>
    string ModelName { get; }

    /// <summary>Gets the sizes the default model accepts; empty when any size within the service's limits is accepted.</summary>
    IReadOnlyList<ImageSize> SupportedSizes { get; }

    /// <summary>Generates one image.</summary>
    /// <param name="prompt">What the image shows.</param>
    /// <param name="size">The pixel size.</param>
    /// <param name="options">Per-call options; null for the defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The image bytes, their media type, the pixel size and the usage.</returns>
    /// <exception cref="ArgumentException">The prompt is empty, or the size is one the model does not offer.</exception>
    /// <exception cref="LlmHttpException">The service refused the request.</exception>
    /// <exception cref="InvalidOperationException">The service answered without an image.</exception>
    Task<GeneratedImage> GenerateImageAsync(
        string prompt,
        ImageSize size,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default);
}
