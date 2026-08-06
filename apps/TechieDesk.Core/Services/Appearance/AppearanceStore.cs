using TechieDesk.Services.Data;

namespace TechieDesk.Services.Appearance;

/// <summary>
/// Stores the appearance choices in the app database (REQ-UI-038 / BRD-90).
/// </summary>
/// <remarks>
/// <para>
/// The store of record is <see cref="IInstanceSettingRepository"/> — the same table
/// <c>UpdatePreferencesStore</c> uses, for the same reason. The app database lives in the REQ-FN-037
/// per-user data directory, so a chosen theme survives an update that replaces the application, and
/// nothing is written inside the read-only <c>.app</c> bundle.
/// </para>
/// <para>
/// The WebView's <c>localStorage</c> is NOT the store of record, although the head does mirror the
/// resolved theme there. That mirror exists only so the first paint of the next launch is already
/// the right colour instead of flashing white while the database is opened; it is a cache that is
/// rewritten from this store on every load, and deleting it changes nothing but that first frame.
/// </para>
/// <para>
/// A missing row means "never chosen", which is why <see cref="LoadAsync"/> does not write. Freezing
/// the default into the database on first read would make the shipped default unchangeable for every
/// existing install.
/// </para>
/// </remarks>
public sealed class AppearanceStore : IAppearanceStore
{
    /// <summary>Setting key for the theme mode.</summary>
    public const string ThemeModeKey = "AppearanceThemeMode";

    /// <summary>Setting key for the accent colour.</summary>
    public const string AccentKey = "AppearanceAccent";

    private readonly IInstanceSettingRepository settings;

    /// <summary>Initializes a new instance of the <see cref="AppearanceStore"/> class.</summary>
    /// <param name="settings">Instance-setting persistence.</param>
    public AppearanceStore(IInstanceSettingRepository settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc />
    public async Task<AppearanceSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var storedMode = await settings.GetAsync(ThemeModeKey).ConfigureAwait(false);
        var storedAccent = await settings.GetAsync(AccentKey).ConfigureAwait(false);

        // Enum.TryParse is case-insensitive and rejects anything the enum does not name, so a value
        // written by a future version degrades to the default instead of throwing on startup.
        var mode = Enum.TryParse<ThemeMode>(storedMode, ignoreCase: true, out var parsed)
                   && Enum.IsDefined(parsed)
            ? parsed
            : AppearanceSettings.Defaults.Mode;

        // Resolve normalises an unknown key to the product accent, so what comes back is always a
        // key the palette actually has.
        var accent = AccentPalette.Resolve(storedAccent).Key;

        return new AppearanceSettings(mode, accent);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        AppearanceSettings appearance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        await settings.SetAsync(ThemeModeKey, appearance.Mode.ToString()).ConfigureAwait(false);
        await settings.SetAsync(AccentKey, AccentPalette.Resolve(appearance.AccentKey).Key)
            .ConfigureAwait(false);
    }
}
