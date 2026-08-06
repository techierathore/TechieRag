namespace TechieRag.Models;

/// <summary>An image supplied as chat input (REQ-RAG-039 / BRD-120).</summary>
/// <remarks>
/// <para>An image is carried either <b>inline</b> as base64 bytes or <b>by reference</b> as an
/// absolute URL. Both shapes exist because the providers differ: Anthropic and Gemini prefer
/// inline bytes, OpenAI-compatible endpoints accept a URL or a <c>data:</c> URI, and Ollama accepts
/// bare base64 only. Whichever shape the caller supplies, every provider that can encode images at
/// all will encode it — a URL is downgraded to nothing only where the wire format has no way to
/// express it, and that case throws rather than silently sending a text-only prompt.</para>
/// </remarks>
public sealed class ChatImage
{
    /// <summary>Gets the IANA media type of the image, e.g. <c>image/png</c>.</summary>
    public required string MediaType { get; init; }

    /// <summary>Gets the base64-encoded image bytes, or null when the image is referenced by URL.</summary>
    public string? Base64Data { get; init; }

    /// <summary>Gets the absolute URL the provider should fetch, or null when the bytes are inline.</summary>
    public Uri? Url { get; init; }

    /// <summary>Gets whether the image bytes travel in the request rather than being fetched by the provider.</summary>
    public bool IsInline => Base64Data is not null;

    /// <summary>Creates an inline image from raw bytes.</summary>
    /// <param name="bytes">The encoded image file's bytes (PNG, JPEG, GIF or WebP data — not a raw bitmap).</param>
    /// <param name="mediaType">IANA media type, e.g. <c>image/png</c>.</param>
    /// <returns>An inline image.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is empty, or <paramref name="mediaType"/> is not an image type.</exception>
    public static ChatImage FromBytes(byte[] bytes, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            throw new ArgumentException("An image must have bytes.", nameof(bytes));
        }

        return new ChatImage
        {
            MediaType = ValidateMediaType(mediaType),
            Base64Data = Convert.ToBase64String(bytes)
        };
    }

    /// <summary>Creates an inline image from already-encoded base64 text.</summary>
    /// <param name="base64Data">Base64 of the encoded image file. A <c>data:</c> URI prefix is accepted and stripped.</param>
    /// <param name="mediaType">IANA media type, e.g. <c>image/jpeg</c>.</param>
    /// <returns>An inline image.</returns>
    /// <exception cref="ArgumentException"><paramref name="base64Data"/> is blank, or <paramref name="mediaType"/> is not an image type.</exception>
    public static ChatImage FromBase64(string base64Data, string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Data);

        // Callers routinely paste a whole data: URI out of a browser or an <img src>. Accepting it
        // and stripping the prefix is kinder than a runtime 400 from the provider, which is what
        // passing "data:image/png;base64,iVBOR..." straight through would earn them.
        var payload = base64Data;
        var comma = payload.IndexOf(',');
        if (comma >= 0 && payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            payload = payload[(comma + 1)..];
        }

        return new ChatImage
        {
            MediaType = ValidateMediaType(mediaType),
            Base64Data = payload
        };
    }

    /// <summary>Creates an image the provider fetches for itself.</summary>
    /// <param name="url">Absolute http or https URL of the image.</param>
    /// <param name="mediaType">IANA media type, e.g. <c>image/webp</c>.</param>
    /// <returns>A referenced image.</returns>
    /// <exception cref="ArgumentException"><paramref name="url"/> is relative or not http(s), or <paramref name="mediaType"/> is not an image type.</exception>
    public static ChatImage FromUrl(Uri url, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri)
        {
            throw new ArgumentException("An image URL must be absolute.", nameof(url));
        }

        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                $"An image URL must be http or https, not '{url.Scheme}'. A file path is a local read the provider cannot perform — load the bytes and use FromBytes instead.",
                nameof(url));
        }

        return new ChatImage
        {
            MediaType = ValidateMediaType(mediaType),
            Url = url
        };
    }

    /// <summary>Renders the image as a <c>data:</c> URI for wire formats that take one.</summary>
    /// <returns>A <c>data:{mediaType};base64,{data}</c> URI.</returns>
    /// <exception cref="InvalidOperationException">The image is referenced by URL and has no inline bytes.</exception>
    public string ToDataUri()
    {
        if (Base64Data is null)
        {
            throw new InvalidOperationException(
                "This image is a URL reference and has no inline bytes to encode as a data URI.");
        }

        return $"data:{MediaType};base64,{Base64Data}";
    }

    private static string ValidateMediaType(string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        // Providers reject a mislabelled part with an opaque 400. Catching it at construction points
        // at the line that built the message rather than at the line that sent it.
        if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"'{mediaType}' is not an image media type. Vision input requires image/png, image/jpeg, image/gif or image/webp.",
                nameof(mediaType));
        }

        return mediaType;
    }
}
