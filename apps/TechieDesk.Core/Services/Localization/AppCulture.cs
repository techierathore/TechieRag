using System.Globalization;

namespace TechieDesk.Services.Localization;

/// <summary>
/// Applies a chosen language to the process (REQ-UI-039 / BRD-91).
/// </summary>
public static class AppCulture
{
    /// <summary>
    /// Makes the supplied language the process-wide UI culture.
    /// </summary>
    /// <param name="language">The language to apply.</param>
    /// <returns>The <see cref="CultureInfo"/> that was applied.</returns>
    /// <remarks>
    /// <para>
    /// Sets the <c>DefaultThreadCurrent*</c> properties as well as the current thread's, and that
    /// pairing is the point. The current-thread values cover the caller; the defaults cover every
    /// thread created afterwards — including the thread pool a BlazorWebView renders continuations
    /// on, which is where a component's <c>IStringLocalizer</c> lookup actually happens. Setting only
    /// the current thread produces a UI that is translated on first render and English after the
    /// first <c>await</c>.
    /// </para>
    /// <para>
    /// Formatting culture and UI culture are set together on purpose: a Hindi UI that prints
    /// <c>7/27/2026</c> and <c>1,024.5</c> is not localized, it is half-localized.
    /// </para>
    /// </remarks>
    public static CultureInfo Apply(AppLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);

        var culture = CultureInfo.GetCultureInfo(language.Culture);

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        return culture;
    }
}
