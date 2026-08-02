namespace TechieDesk.Services.Appearance;

/// <summary>
/// The persisted appearance choices (REQ-UI-038 / BRD-90).
/// </summary>
/// <param name="Mode">The chosen theme mode.</param>
/// <param name="AccentKey">The chosen accent key; see <see cref="AccentPalette"/>.</param>
public sealed record AppearanceSettings(ThemeMode Mode, string AccentKey)
{
    /// <summary>Gets the settings applied to an install that has chosen nothing.</summary>
    public static AppearanceSettings Defaults { get; } =
        new(ThemeMode.System, AccentPalette.DefaultKey);

    /// <summary>Gets the resolved accent for <see cref="AccentKey"/>.</summary>
    public AccentColor Accent => AccentPalette.Resolve(AccentKey);
}
