namespace TechieDesk.Services.Localization;

/// <summary>
/// The languages TechieDesk ships translations for (REQ-UI-039 / BRD-91: English and Hindi).
/// </summary>
/// <remarks>
/// <para>
/// Hindi is the one addition, and it is NAMED by the requirement rather than inferred. BRD-91 used
/// to say "<c>en</c> plus at least 2 locales" without naming any, so an earlier build shipped German
/// and French on no recorded basis — nobody had chosen them against the audience. The owner named
/// the set on 2026-07-29: the product's target audience is India, so the shipped locales are
/// <c>en</c> + <c>hi</c> and <c>de</c>/<c>fr</c> are withdrawn.
/// </para>
/// <para>
/// Hindi is written in DEVANAGARI and is left-to-right, so it exercises a non-Latin script — and
/// therefore the WebView's font fallback, where a missing glyph renders as a tofu box — without
/// requiring the RTL mirroring work that Arabic or Hebrew would. RTL remains deferred (BRD-91).
/// </para>
/// <para>
/// The list is the authority for the picker and for what the resource fallback can land on. Adding a
/// language means adding an entry here AND an <c>AppStrings.{culture}.resx</c>; an entry without a
/// resource file silently falls back to English, which is why <c>LocalizationTests</c> asserts every
/// listed culture actually resolves a translated string.
/// </para>
/// <para>
/// <b>REQ-UI-055 / BRD-91 — audited and deliberately left alone.</b> Neither name below is a
/// localization defect. <c>NativeName</c> is the ENDONYM and is the one place a language is named in
/// ITSELF rather than in the reader's language (REQ-UI-039): somebody stranded in a UI they cannot
/// read is hunting for "हिन्दी", and translating that to "Hindi" for a Hindi reader would remove the
/// only string on the screen they were looking for. It is what the picker draws.
/// <c>EnglishName</c> is not drawn at all — it exists for logs and diagnostics, which is
/// machine-facing by definition. Routing either through a resource file would make the language
/// picker unusable for the exact person it exists for.
/// </para>
/// </remarks>
public static class SupportedLanguages
{
    /// <summary>The culture used when nothing has been chosen, and the resource fallback.</summary>
    public const string DefaultCulture = "en";

    private static readonly IReadOnlyList<AppLanguage> AllLanguages =
    [
        new AppLanguage("en", "English", "English"),
        new AppLanguage("hi", "Hindi", "हिन्दी")
    ];

    /// <summary>Gets every offered language, English first.</summary>
    public static IReadOnlyList<AppLanguage> All => AllLanguages;

    /// <summary>Gets the language used when nothing has been chosen.</summary>
    public static AppLanguage Default => AllLanguages[0];

    /// <summary>
    /// Resolves a culture name to an offered language, falling back to <see cref="Default"/>.
    /// </summary>
    /// <param name="culture">A culture name, which may carry a region (<c>hi-IN</c>).</param>
    /// <returns>The matching language, or <see cref="Default"/>.</returns>
    /// <remarks>
    /// Matches the neutral culture as well as the exact name, so a machine running <c>hi-IN</c> gets
    /// the Hindi UI rather than dropping to English. Total by design: the value arrives from a
    /// database row or an OS setting, neither of which this app controls.
    /// </remarks>
    public static AppLanguage Resolve(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return Default;
        }

        foreach (var language in AllLanguages)
        {
            if (string.Equals(language.Culture, culture, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        var separator = culture.IndexOf('-', StringComparison.Ordinal);
        if (separator > 0)
        {
            return Resolve(culture[..separator]);
        }

        return Default;
    }

    /// <summary>Determines whether a culture name is one this app ships a translation for.</summary>
    /// <param name="culture">A culture name, which may carry a region.</param>
    /// <returns>True when the culture, or its neutral parent, is offered.</returns>
    /// <remarks>
    /// Distinct from <see cref="Resolve"/>, which is total and answers "what do I render". This
    /// answers "was the request understood", so an unknown culture is false even though rendering it
    /// still works — that is what lets the picker show English as a CHOICE rather than as a silent
    /// fallback that looks like the choice failed.
    /// </remarks>
    public static bool IsSupported(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return false;
        }

        var neutral = culture.IndexOf('-', StringComparison.Ordinal) is var separator && separator > 0
            ? culture[..separator]
            : culture;

        foreach (var language in AllLanguages)
        {
            if (string.Equals(language.Culture, neutral, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
