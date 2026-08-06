using System.Globalization;
using TechieDesk.Services.Data;

namespace TechieDesk.Services.Localization;

/// <summary>
/// Stores the chosen UI language in the app database (REQ-UI-039 / BRD-91).
/// </summary>
/// <remarks>
/// <para>
/// Same store of record as the appearance and branding settings — the
/// <see cref="IInstanceSettingRepository"/> table in the REQ-FN-037 per-user data directory.
/// </para>
/// <para>
/// With nothing stored, the OS language is used when TechieDesk ships it. That is deliberately not
/// the same as writing the OS language into the database on first run: a machine later switched to
/// Hindi should follow, and it can only do that while the row is still absent.
/// </para>
/// </remarks>
public sealed class LanguageStore : ILanguageStore
{
    /// <summary>Setting key for the chosen language.</summary>
    public const string LanguageKey = "AppearanceLanguage";

    private readonly IInstanceSettingRepository settings;
    private readonly Func<string> systemCultureProvider;

    /// <summary>Initializes a new instance of the <see cref="LanguageStore"/> class.</summary>
    /// <param name="settings">Instance-setting persistence.</param>
    public LanguageStore(IInstanceSettingRepository settings)
        : this(settings, static () => CultureInfo.InstalledUICulture.Name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageStore"/> class with an explicit source
    /// for the operating system's language.
    /// </summary>
    /// <param name="settings">Instance-setting persistence.</param>
    /// <param name="systemCultureProvider">Supplies the OS UI culture name.</param>
    /// <remarks>
    /// The seam exists so the OS-follows behaviour can be asserted. Reading
    /// <see cref="CultureInfo.InstalledUICulture"/> directly would make that test depend on the
    /// machine running it, which is the same as not testing it.
    /// </remarks>
    public LanguageStore(IInstanceSettingRepository settings, Func<string> systemCultureProvider)
    {
        this.settings = settings;
        this.systemCultureProvider = systemCultureProvider;
    }

    /// <inheritdoc />
    public async Task<AppLanguage> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settings.GetAsync(LanguageKey).ConfigureAwait(false);
        return Resolve(stored);
    }

    // The one place the stored value / OS language / English precedence is expressed, so the sync
    // and async loads cannot drift apart.
    private AppLanguage Resolve(string? stored)
    {
        if (SupportedLanguages.IsSupported(stored))
        {
            return SupportedLanguages.Resolve(stored);
        }

        var system = SafeSystemCulture();
        return SupportedLanguages.IsSupported(system)
            ? SupportedLanguages.Resolve(system)
            : SupportedLanguages.Default;
    }

    /// <inheritdoc />
    public AppLanguage Load() => Resolve(settings.Get(LanguageKey));

    /// <inheritdoc />
    public async Task SaveAsync(
        AppLanguage language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(language);

        await settings.SetAsync(LanguageKey, SupportedLanguages.Resolve(language.Culture).Culture)
            .ConfigureAwait(false);
    }

    // A culture lookup must never be able to stop the app from choosing a language. Anything that
    // throws here is treated as "the OS did not say", which lands on English.
    private string SafeSystemCulture()
    {
        try
        {
            return systemCultureProvider() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
