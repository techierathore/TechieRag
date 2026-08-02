using TechieDesk.Services.Appearance;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Appearance;

/// <summary>
/// REQ-UI-038 (BRD-90) + REQ-NFR-005: the selectable accents, and the accessibility floor every one
/// of them has to clear.
/// </summary>
public sealed class AccentPaletteTests
{
    /// <summary>WCAG 2.1 AA 1.4.3 minimum contrast for normal-size text.</summary>
    private const double AaTextMinimum = 4.5;

    /// <summary>The five swatches from the Branding tab of the mockup are all offered.</summary>
    [Fact]
    public void OffersTheFiveMockupSwatches()
    {
        var keys = AccentPalette.All.Select(accent => accent.Key).ToArray();

        Assert.Equal(["indigo", "blue", "green", "amber", "red"], keys);
    }

    /// <summary>The default accent is the approved product indigo.</summary>
    [Fact]
    public void DefaultsToTheApprovedIndigo()
    {
        Assert.Equal("indigo", AccentPalette.Default.Key);
        Assert.Equal("#4F46E5", AccentPalette.Default.LightHex);
        Assert.Equal("#818CF8", AccentPalette.Default.DarkHex);
    }

    /// <summary>
    /// Every accent's button label clears the AA floor in BOTH palettes.
    /// <para>
    /// This is the assertion the palette exists for. White on the approved green measures 3.30:1 and
    /// on the approved amber 3.19:1 — so a palette that stored one hex per accent and assumed white
    /// text would ship two unreadable accents. The foreground is chosen per accent, and this proves
    /// each pairing rather than trusting the choice.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryAccentClearsTheContrastFloor(bool dark)
    {
        foreach (var accent in AccentPalette.All)
        {
            var ratio = ColorMath.ContrastRatio(
                accent.PrimaryFor(dark), accent.PrimaryForegroundFor(dark));

            Assert.True(
                ratio >= AaTextMinimum,
                $"{accent.Key} ({(dark ? "dark" : "light")}) measures {ratio:F2}:1 against its own " +
                $"foreground; WCAG AA 1.4.3 needs {AaTextMinimum}:1.");
        }
    }

    /// <summary>
    /// The swatch hex and the OKLCH token describe the same colour. The hex is what the operator
    /// clicks and the OKLCH is what the browser renders, so a drift between them would show the user
    /// one colour and apply another.
    /// </summary>
    [Fact]
    public void SwatchHexMatchesTheToken()
    {
        foreach (var accent in AccentPalette.All)
        {
            AssertSameColor(accent.LightHex, accent.PrimaryFor(false), $"{accent.Key} light");
            AssertSameColor(accent.DarkHex, accent.PrimaryFor(true), $"{accent.Key} dark");
        }
    }

    /// <summary>
    /// No two accents look the same. The swatch row identifies each choice by colour alone, so two
    /// entries sharing a hex would render as two buttons a user cannot tell apart — and two entries
    /// sharing a key would make <c>Resolve</c> ambiguous about which one a stored row means.
    /// </summary>
    [Fact]
    public void EveryAccentIsDistinct()
    {
        var keys = AccentPalette.All.Select(accent => accent.Key.ToLowerInvariant()).ToArray();
        var lightHexes = AccentPalette.All.Select(accent => accent.LightHex.ToUpperInvariant()).ToArray();
        var darkHexes = AccentPalette.All.Select(accent => accent.DarkHex.ToUpperInvariant()).ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(lightHexes.Length, lightHexes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(darkHexes.Length, darkHexes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Every accent carries a display name and a resource-key-safe identifier. The panel builds its
    /// .resx key by upper-casing the first character of the key ("indigo" → "AccentIndigo"), so a key
    /// that is empty, padded or not ASCII letters would silently produce a lookup that misses.
    /// </summary>
    [Fact]
    public void EveryAccentKeyCanBecomeAResourceKey()
    {
        foreach (var accent in AccentPalette.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(accent.Key));
            Assert.False(string.IsNullOrWhiteSpace(accent.DisplayNameKey));
            Assert.Equal(accent.Key, accent.Key.Trim());
            Assert.True(
                accent.Key.All(char.IsAsciiLetter),
                $"Accent key '{accent.Key}' is not ASCII letters, so 'Accent' + it is not a usable " +
                "resource key.");
        }
    }

    /// <summary>An unknown key resolves to the product accent instead of throwing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("chartreuse")]
    public void ResolvesAnUnknownKeyToTheDefault(string? key) =>
        Assert.Equal(AccentPalette.Default, AccentPalette.Resolve(key));

    /// <summary>Keys resolve case-insensitively, so a hand-edited database row still works.</summary>
    [Fact]
    public void ResolvesKeysCaseInsensitively() =>
        Assert.Equal("green", AccentPalette.Resolve("GREEN").Key);

    // Compared by relative luminance rather than by channel: the two representations round-trip
    // through floating-point matrices, so an exact match is not available and is not what matters —
    // what matters is that they are not DIFFERENT colours.
    private static void AssertSameColor(string hex, string oklch, string label)
    {
        var fromHex = ColorMath.RelativeLuminanceOfHex(hex);
        var fromToken = ColorMath.RelativeLuminance(oklch);

        Assert.True(
            Math.Abs(fromHex - fromToken) < 0.01,
            $"{label}: swatch {hex} has luminance {fromHex:F4} but the token {oklch} has " +
            $"{fromToken:F4}.");
    }
}
