using System.Globalization;

namespace TechieRag.Models;

/// <summary>An image size in pixels (REQ-RAG-155 / BRD-213).</summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
public readonly record struct ImageSize(int Width, int Height)
{
    /// <summary>1024 by 1024, the size every shipped image provider offers.</summary>
    public static ImageSize Square1024 => new(1024, 1024);

    /// <summary>Parses <c>WIDTHxHEIGHT</c>, for example <c>1536x1024</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The size.</returns>
    /// <exception cref="FormatException">The text is not two positive whole numbers joined by <c>x</c>.</exception>
    public static ImageSize Parse(string text)
    {
        var parts = (text ?? string.Empty).Split('x', 'X');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            && width > 0 && height > 0)
        {
            return new ImageSize(width, height);
        }

        throw new FormatException($"'{text}' is not an image size such as 1024x1024.");
    }

    /// <summary>Returns the size as <c>WIDTHxHEIGHT</c>.</summary>
    /// <returns>The text, for example <c>1024x1024</c>.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}");
}
