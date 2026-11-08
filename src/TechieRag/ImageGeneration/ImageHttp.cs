using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TechieRag.Llm;
using TechieRag.Models;

namespace TechieRag.ImageGeneration;

/// <summary>The HTTP work the image providers share (REQ-RAG-155 / BRD-213).</summary>
internal static class ImageHttp
{
    /// <summary>The timeout of a call that names none: image models are slow.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Builds the client a provider owns, with the per-call timeout enforced by token instead.</summary>
    /// <param name="handler">A handler to send through (tests), or null for the default.</param>
    /// <returns>The client.</returns>
    public static HttpClient CreateClient(HttpMessageHandler? handler) =>
        handler is null
            ? new HttpClient { Timeout = Timeout.InfiniteTimeSpan }
            : new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Checks the arguments every provider checks.</summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="size">The size.</param>
    /// <param name="supported">The sizes the model accepts; empty for any.</param>
    /// <param name="modelName">The model, for the message.</param>
    /// <exception cref="ArgumentException">The prompt is empty, or the size is not accepted.</exception>
    public static void CheckArguments(string prompt, ImageSize size, IReadOnlyList<ImageSize> supported, string modelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentException($"The image size {size} is not a positive width and height.", nameof(size));
        }

        if (supported.Count > 0 && !supported.Contains(size))
        {
            throw new ArgumentException(
                $"{modelName} does not make {size} images; it makes {string.Join(", ", supported)}.", nameof(size));
        }
    }

    /// <summary>Posts JSON and returns the parsed answer, throwing the library's HTTP error on a refusal.</summary>
    /// <param name="client">The client.</param>
    /// <param name="url">The URL; never carries the key.</param>
    /// <param name="body">The request body.</param>
    /// <param name="authorize">Adds the key to the request's headers.</param>
    /// <param name="timeout">The call's timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The answer.</returns>
    public static async Task<JsonNode> PostJsonAsync(
        HttpClient client,
        string url,
        JsonObject body,
        Action<HttpRequestHeaders> authorize,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        authorize(request.Headers);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
        await LlmHttpGuard.EnsureSuccessAsync(response, timeoutSource.Token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
        try
        {
            return JsonNode.Parse(text) ?? throw new InvalidOperationException("The image service answered with an empty body.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The image service answered with a body that is not JSON.", exception);
        }
    }

    /// <summary>Decodes base64 image data, refusing an empty or malformed answer.</summary>
    /// <param name="base64">The data.</param>
    /// <param name="service">The service, for the message.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Decode(string? base64, string service)
    {
        if (string.IsNullOrEmpty(base64))
        {
            throw new InvalidOperationException($"{service} answered without an image.");
        }

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{service} answered with image data that is not base64.", exception);
        }
    }

    /// <summary>Reads an optional integer from a JSON node.</summary>
    /// <param name="node">The node, or null.</param>
    /// <returns>The integer, or null.</returns>
    public static int? Int(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    /// <summary>Gets the first element of a JSON array, or null when the node is no array or an empty one.</summary>
    /// <param name="node">The node, or null.</param>
    /// <returns>The first element, or null.</returns>
    public static JsonNode? First(JsonNode? node) => node is JsonArray { Count: > 0 } array ? array[0] : null;

    /// <summary>Joins a base URL and a path.</summary>
    /// <param name="baseUrl">The base URL.</param>
    /// <param name="path">The path.</param>
    /// <returns>The URL.</returns>
    public static string Url(string baseUrl, string path) => baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
}
