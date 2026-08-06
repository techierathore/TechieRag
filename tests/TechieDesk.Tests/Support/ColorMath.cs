using System.Globalization;

namespace TechieDesk.Tests.Support;

/// <summary>
/// Converts the OKLCH design-token strings to sRGB and measures WCAG contrast.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the accessibility assertions in <c>AccentPaletteTests</c> read the VALUES THE
/// BROWSER WILL GET rather than a hex duplicate maintained alongside them. The tokens are authored
/// as <c>oklch(L C H)</c>; a test that asserted against a parallel hex list would keep passing after
/// someone edited only the OKLCH, which is the one edit that matters.
/// </para>
/// <para>
/// The conversion is Björn Ottosson's published Oklab matrices followed by the sRGB transfer
/// function, and the ratio is WCAG 2.1's relative-luminance formula. Both are fixed specifications,
/// so this is arithmetic rather than a re-implementation of anything that could drift.
/// </para>
/// </remarks>
public static class ColorMath
{
    /// <summary>Parses an <c>oklch(L C H)</c> string and returns its WCAG relative luminance.</summary>
    /// <param name="oklch">A CSS <c>oklch()</c> function string.</param>
    /// <returns>The relative luminance, 0..1.</returns>
    /// <exception cref="FormatException">Thrown when the string is not an oklch() triple.</exception>
    public static double RelativeLuminance(string oklch)
    {
        var (red, green, blue) = ToLinearRgb(oklch);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    /// <summary>Returns the WCAG 2.1 contrast ratio between two OKLCH colours.</summary>
    /// <param name="first">The first colour as an <c>oklch()</c> string.</param>
    /// <param name="second">The second colour as an <c>oklch()</c> string.</param>
    /// <returns>The ratio, from 1.0 (identical) to 21.0 (black on white).</returns>
    public static double ContrastRatio(string first, string second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>Returns the WCAG relative luminance of an sRGB hex colour.</summary>
    /// <param name="hex">A colour such as <c>#4F46E5</c>.</param>
    /// <returns>The relative luminance, 0..1.</returns>
    public static double RelativeLuminanceOfHex(string hex)
    {
        var trimmed = hex.TrimStart('#');
        var red = ToLinear(int.Parse(trimmed[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d);
        var green = ToLinear(int.Parse(trimmed.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d);
        var blue = ToLinear(int.Parse(trimmed.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static (double Red, double Green, double Blue) ToLinearRgb(string oklch)
    {
        var open = oklch.IndexOf('(', StringComparison.Ordinal);
        var close = oklch.LastIndexOf(')');
        if (open < 0 || close < open)
        {
            throw new FormatException($"'{oklch}' is not an oklch() function string.");
        }

        var parts = oklch[(open + 1)..close]
            .Split([' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            throw new FormatException($"'{oklch}' does not carry three components.");
        }

        var lightness = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var chroma = double.Parse(parts[1], CultureInfo.InvariantCulture);
        var hue = double.Parse(parts[2], CultureInfo.InvariantCulture) * Math.PI / 180d;

        var a = chroma * Math.Cos(hue);
        var b = chroma * Math.Sin(hue);

        var longCone = Cube(lightness + (0.3963377774 * a) + (0.2158037573 * b));
        var mediumCone = Cube(lightness - (0.1055613458 * a) - (0.0638541728 * b));
        var shortCone = Cube(lightness - (0.0894841775 * a) - (1.2914855480 * b));

        var red = (4.0767416621 * longCone) - (3.3077115913 * mediumCone) + (0.2309699292 * shortCone);
        var green = (-1.2684380046 * longCone) + (2.6097574011 * mediumCone) - (0.3413193965 * shortCone);
        var blue = (-0.0041960863 * longCone) - (0.7034186147 * mediumCone) + (1.7076147010 * shortCone);

        // Clamped because a wide-gamut OKLCH triple can land marginally outside sRGB; the browser
        // clips it the same way before it ever reaches a pixel.
        return (Math.Clamp(red, 0, 1), Math.Clamp(green, 0, 1), Math.Clamp(blue, 0, 1));
    }

    private static double Cube(double value) => value * value * value;

    private static double ToLinear(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
