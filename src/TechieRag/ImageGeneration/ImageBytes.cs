using System.Buffers.Binary;
using TechieRag.Models;

namespace TechieRag.ImageGeneration;

/// <summary>Reads what an encoded image is from its own bytes (REQ-RAG-155 / BRD-213).</summary>
/// <remarks>A service's stated type is not trusted: the media type and the pixel size come from the header, so a
/// caller asking for PNG learns when a service sent something else.</remarks>
internal static class ImageBytes
{
    /// <summary>The PNG signature.</summary>
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Gets the media type of encoded image bytes.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="fallback">The type to report when the bytes are none of PNG, JPEG or WebP.</param>
    /// <returns>The media type.</returns>
    public static string MediaTypeOf(byte[] bytes, string fallback)
    {
        if (bytes.AsSpan().StartsWith(PngSignature))
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return fallback;
    }

    /// <summary>Reads the pixel size from a PNG header.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The size, or null when the bytes are not a PNG with a readable header.</returns>
    public static ImageSize? PngSize(byte[] bytes)
    {
        // Signature (8), IHDR length (4), "IHDR" (4), width (4), height (4).
        if (bytes.Length < 24 || !bytes.AsSpan().StartsWith(PngSignature) || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        return width > 0 && height > 0 ? new ImageSize(width, height) : null;
    }

    /// <summary>Builds the result for decoded bytes.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="statedType">The type the service stated, or null.</param>
    /// <param name="asked">The size asked for.</param>
    /// <param name="usage">The usage.</param>
    /// <returns>The image.</returns>
    public static GeneratedImage Result(byte[] bytes, string? statedType, ImageSize asked, ImageGenerationUsage usage) =>
        new(bytes, MediaTypeOf(bytes, statedType ?? "application/octet-stream"), PngSize(bytes) ?? asked, usage);
}
