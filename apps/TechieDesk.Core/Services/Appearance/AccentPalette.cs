namespace TechieDesk.Services.Appearance;

/// <summary>
/// The five selectable accent colours from the Branding tab of docs/mockups/admin-settings.html
/// (REQ-UI-038 / BRD-90).
/// </summary>
/// <remarks>
/// The list is closed on purpose. A free colour picker cannot be given an accessible foreground —
/// there is no text colour that reads on every hue a user might paste in — so REQ-NFR-005 could only
/// be met by refusing most of what such a control would offer. Five vetted pairs, each measured
/// against the AA 1.4.3 floor in both palettes, is the honest version of that feature.
/// <para>
/// <b>REQ-UI-055 / BRD-91.</b> Every <see cref="AccentColor.Key"/> here is PERSISTED — REQ-UI-038
/// stores the chosen accent by name in the app database and <see cref="Resolve"/> reads it back — so
/// the keys are invariant, lower-case ASCII, whatever culture the app runs in, and so are the OKLCH
/// and hex colour strings, which are CSS values. Only <see cref="AccentColor.DisplayNameKey"/>
/// reaches the reader, and it is a resource key rather than a name.
/// </para>
/// </remarks>
public static class AccentPalette
{
    /// <summary>The key of the accent applied when nothing has been chosen.</summary>
    public const string DefaultKey = "indigo";

    /// <summary>
    /// Near-black ink used as the foreground on light accents. Matches the <c>#10131F</c> dark
    /// sidebar from the mockups so a primary button in dark mode reads as part of the same palette.
    /// </summary>
    private const string DarkInk = "oklch(0.1899 0.0249 273.04)";

    /// <summary>White ink used as the foreground on accents dark enough to carry it.</summary>
    private const string LightInk = "oklch(0.9850 0 0)";

    private static readonly IReadOnlyList<AccentColor> AllAccents =
    [
        // Approved product accent. #4F46E5, white text 6.29:1.
        new AccentColor(
            "indigo", "AccentIndigo",
            "#4F46E5", "oklch(0.5106 0.2301 276.97)", LightInk,
            "#818CF8", "oklch(0.6801 0.1583 276.93)", DarkInk),

        // #2563EB, white text 5.17:1.
        new AccentColor(
            "blue", "AccentBlue",
            "#2563EB", "oklch(0.5461 0.2152 262.88)", LightInk,
            "#60A5FA", "oklch(0.7137 0.1434 254.62)", DarkInk),

        // #16A34A. White text measures 3.30:1 and FAILS AA, so this accent carries dark ink
        // (5.97:1) instead. See AccentColor for why the foreground travels with the colour.
        new AccentColor(
            "green", "AccentGreen",
            "#16A34A", "oklch(0.6271 0.1699 149.21)", DarkInk,
            "#4ADE80", "oklch(0.8003 0.1821 151.71)", DarkInk),

        // #D97706. White text measures 3.19:1 and FAILS AA; dark ink measures 5.30:1.
        new AccentColor(
            "amber", "AccentAmber",
            "#D97706", "oklch(0.6658 0.1574 58.32)", DarkInk,
            "#FBBF24", "oklch(0.8369 0.1644 84.43)", DarkInk),

        // #DC2626, white text 4.83:1.
        new AccentColor(
            "red", "AccentRed",
            "#DC2626", "oklch(0.5771 0.2152 27.33)", LightInk,
            "#F87171", "oklch(0.7106 0.1661 22.22)", DarkInk)
    ];

    /// <summary>Gets every selectable accent, in the order the Branding swatch row shows them.</summary>
    public static IReadOnlyList<AccentColor> All => AllAccents;

    /// <summary>Gets the accent applied when nothing has been chosen.</summary>
    public static AccentColor Default => AllAccents[0];

    /// <summary>
    /// Resolves an accent by key, falling back to <see cref="Default"/> for an unknown, empty or
    /// null key.
    /// </summary>
    /// <param name="key">The persisted accent key.</param>
    /// <returns>The matching accent, or <see cref="Default"/>.</returns>
    /// <remarks>
    /// Deliberately total rather than throwing. The key comes out of a database row that a previous
    /// version wrote, so an unrecognised value is an expected downgrade case — the app must render
    /// in the product accent, not refuse to paint.
    /// </remarks>
    public static AccentColor Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Default;
        }

        foreach (var accent in AllAccents)
        {
            if (string.Equals(accent.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return accent;
            }
        }

        return Default;
    }
}
