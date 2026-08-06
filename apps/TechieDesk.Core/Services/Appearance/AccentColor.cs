namespace TechieDesk.Services.Appearance;

/// <summary>
/// One selectable accent colour (REQ-UI-038 / BRD-90) — the value bound to the <c>--primary</c>,
/// <c>--sidebar-primary</c> and <c>--ring</c> design tokens.
/// </summary>
/// <remarks>
/// <para>
/// Each accent carries FOUR colours, not one. A brand colour is only half of a token: the other half
/// is the text colour that has to sit on top of it, and the answer differs per hue. Measured against
/// WCAG 2.1 AA 1.4.3 (REQ-NFR-005), white on the approved indigo <c>#4F46E5</c> is 6.29:1 and passes,
/// but white on the approved green <c>#16A34A</c> is 3.30:1 and white on the approved amber
/// <c>#D97706</c> is 3.19:1 — both fail. Storing a single hex and assuming white text would have
/// shipped two accents whose every primary button was unreadable, so the foreground travels with the
/// colour and is chosen per accent.
/// </para>
/// <para>
/// The dark-mode value is likewise not derived. <c>#818CF8</c> is the approved dark indigo from
/// docs/mockups/workspace-chat.html; the remaining four are the same one-step-lighter Tailwind ramp
/// position, so the whole palette keeps a consistent weight against the <c>#0D1017</c> dark canvas.
/// </para>
/// </remarks>
/// <param name="Key">Stable identifier persisted in the app database.</param>
/// <param name="DisplayNameKey">
/// Resource key for the name shown beside the swatch (REQ-UI-055 / BRD-91). A KEY rather than a name:
/// this file cannot see the reader's language, and the swatch row's only text is the accessible name
/// a screen-reader user hears, so English here would be the whole control in the wrong language.
/// It is deliberately NOT derived from <paramref name="Key"/> — <c>AppearancePanel</c> used to build
/// it as <c>"Accent" + Capitalise(Key)</c>, which quietly made the PERSISTED identifier part of a
/// resource contract, so renaming a resource or adding an accent whose key did not capitalise cleanly
/// would have shown the raw key on screen. This is the same trap <c>QdrantAdmin</c>'s endpoint kind
/// fell into from the other direction, and the two now travel together instead of being reconstructed
/// from one another.
/// </param>
/// <param name="LightHex">The swatch colour, and the light-mode <c>--primary</c>, as sRGB hex.</param>
/// <param name="LightPrimary">Light-mode <c>--primary</c> as an OKLCH function string.</param>
/// <param name="LightPrimaryForeground">Light-mode <c>--primary-foreground</c> as OKLCH.</param>
/// <param name="DarkHex">The dark-mode <c>--primary</c> as sRGB hex.</param>
/// <param name="DarkPrimary">Dark-mode <c>--primary</c> as an OKLCH function string.</param>
/// <param name="DarkPrimaryForeground">Dark-mode <c>--primary-foreground</c> as OKLCH.</param>
public sealed record AccentColor(
    string Key,
    string DisplayNameKey,
    string LightHex,
    string LightPrimary,
    string LightPrimaryForeground,
    string DarkHex,
    string DarkPrimary,
    string DarkPrimaryForeground)
{
    /// <summary>Gets the <c>--primary</c> value for the supplied resolved theme.</summary>
    /// <param name="dark">True for the dark palette, false for the light palette.</param>
    /// <returns>An OKLCH function string.</returns>
    public string PrimaryFor(bool dark) => dark ? DarkPrimary : LightPrimary;

    /// <summary>Gets the <c>--primary-foreground</c> value for the supplied resolved theme.</summary>
    /// <param name="dark">True for the dark palette, false for the light palette.</param>
    /// <returns>An OKLCH function string.</returns>
    public string PrimaryForegroundFor(bool dark) =>
        dark ? DarkPrimaryForeground : LightPrimaryForeground;

    /// <summary>Gets the swatch hex for the supplied resolved theme.</summary>
    /// <param name="dark">True for the dark palette, false for the light palette.</param>
    /// <returns>An sRGB hex string including the leading <c>#</c>.</returns>
    public string HexFor(bool dark) => dark ? DarkHex : LightHex;
}
