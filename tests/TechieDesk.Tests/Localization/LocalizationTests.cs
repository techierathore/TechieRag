using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using TechieDesk.Resources;
using TechieDesk.Services.Appearance;
using TechieDesk.Services.Localization;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// REQ-UI-039 / REQ-UI-050 (BRD-91): the .resx pipeline, the offered languages (en + hi) and where
/// the choice is stored.
/// </summary>
/// <remarks>
/// These tests resolve strings through the SAME <see cref="IStringLocalizer{T}"/> the components use,
/// so they fail the way the app would: a resource base name that no longer matches the embedded
/// resource does not throw — <c>ResourceManagerStringLocalizer</c> returns the KEY as the value and
/// flags <c>ResourceNotFound</c>. Asserting on the value alone would therefore pass forever if the
/// English text ever equalled its own key, which is why the flag is asserted too.
/// </remarks>
public sealed class LocalizationTests : IDisposable
{
    private readonly ServiceProvider services;
    private readonly IStringLocalizer<AppStrings> localizer;
    private readonly CultureInfo originalCulture = CultureInfo.CurrentUICulture;

    /// <summary>Builds a localization container equivalent to the app's.</summary>
    public LocalizationTests()
    {
        services = new ServiceCollection()
            .AddLogging()
            .AddLocalization()
            .BuildServiceProvider();

        localizer = services.GetRequiredService<IStringLocalizer<AppStrings>>();
    }

    /// <summary>Every offered language actually resolves a translated string.</summary>
    /// <param name="culture">The culture to render in.</param>
    /// <param name="expected">The expected translation of <c>ThemeLabel</c>.</param>
    [Theory]
    [InlineData("en", "Theme")]
    [InlineData("hi", "थीम")]
    public void ResolvesEveryShippedLanguage(string culture, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        var value = localizer["ThemeLabel"];

        Assert.False(value.ResourceNotFound, $"The {culture} resource set was not found.");
        Assert.Equal(expected, value.Value);
    }

    /// <summary>
    /// A regional variant lands on its neutral translation instead of falling back to English —
    /// which is what a machine set to hi-IN actually is, and hi-IN is the common case rather than
    /// the exotic one: an Indian install is far likelier to report the region than the bare neutral.
    /// </summary>
    [Theory]
    [InlineData("hi-IN", "थीम")]
    [InlineData("en-GB", "Theme")]
    public void ResolvesARegionalVariant(string culture, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        Assert.Equal(expected, localizer["ThemeLabel"].Value);
    }

    /// <summary>A language with no resource set falls back to English rather than to the key.</summary>
    [Fact]
    public void FallsBackToEnglishForAnUnshippedLanguage()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja");

        Assert.Equal("Theme", localizer["ThemeLabel"].Value);
    }

    /// <summary>
    /// Every key present in English is present in Hindi. A partly translated resource set does not
    /// fail — it silently renders English in the middle of a Hindi screen — so the only way this is
    /// caught is by comparing the key sets.
    /// </summary>
    /// <param name="culture">The translated culture to compare against English.</param>
    [Theory]
    [InlineData("hi")]
    public void TranslationsCoverEveryEnglishKey(string culture)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        var englishKeys = KeysFor(includeParentCultures: true);

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var translatedKeys = KeysFor(includeParentCultures: false);

        var missing = englishKeys.Except(translatedKeys).OrderBy(key => key, StringComparer.Ordinal);
        Assert.Empty(missing);
    }

    /// <summary>
    /// No translated string is left identical to its English source. Hindi is written in
    /// Devanagari, so a value that still matches the English byte for byte is a copy-paste that was
    /// never translated — there is no legitimate case, not even for the product nouns, because those
    /// sit inside a Devanagari sentence ("Qdrant एडमिन").
    /// </summary>
    /// <param name="culture">The translated culture to inspect.</param>
    [Theory]
    [InlineData("hi")]
    public void TranslationsAreNotCopiesOfTheEnglish(string culture)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        var english = localizer.GetAllStrings(includeParentCultures: true)
            .ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        var untranslated = localizer.GetAllStrings(includeParentCultures: false)
            .Where(entry => english.TryGetValue(entry.Name, out var source)
                            && string.Equals(source, entry.Value, StringComparison.Ordinal)
                            )
            .Select(entry => entry.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Empty(untranslated);
    }

    /// <summary>
    /// Every key the SHIPPED screens actually ask for resolves in every shipped language.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// <para>
    /// <c>TranslationsCoverEveryEnglishKey</c> compares the resource sets to EACH OTHER, so it stays
    /// green when a component is changed to ask for a key that no resource set has — the lookup then
    /// degrades to the key name and the screen renders "AppSettingsTabBranding" as a tab label. This
    /// list is the other direction: it is the set of keys the razor components name today, asserted
    /// against the resources. Renaming a key in a .resx without updating the component (or the other
    /// way round) fails here.
    /// </para>
    /// <para>
    /// Kept as an explicit list rather than scraped from the .razor files at runtime: the head is a
    /// MAUI project this net10.0 test project cannot reference, so any scrape would be a brittle
    /// relative-path walk that passes silently when the path is wrong.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void ResolvesEveryKeyTheShippedScreensAskFor(string culture)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        // Not ResourceNotFound: a key missing from hi.resx but present in the neutral set resolves
        // to the ENGLISH text with ResourceNotFound false, which is exactly the half-translated
        // screen this test exists to catch. The culture's OWN key set is the only honest check.
        var own = KeysFor(includeParentCultures: culture == SupportedLanguages.DefaultCulture);

        foreach (var key in KeysUsedByShippedScreens)
        {
            Assert.True(
                own.Contains(key),
                $"{key} is asked for by a shipped component but is missing from the {culture} " +
                "resources, so that screen renders English (or the key name) in a " +
                $"{culture} window.");
            Assert.False(
                string.IsNullOrWhiteSpace(localizer[key].Value),
                $"{key} resolves to an empty string in {culture}.");
        }
    }

    /// <summary>
    /// Every key ANY razor component names as a literal resolves in every shipped language.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// <para>
    /// The scraped counterpart to <see cref="ResolvesEveryKeyTheShippedScreensAskFor"/>. That test's
    /// hand-written list was tractable while seven components were translated; REQ-UI-050's page
    /// tranche took six 1,000-line screens at once, and a literal list of that size stops being
    /// reviewed and starts being appended to. This reads the keys off the components themselves, so
    /// a key added to a screen and forgotten in <c>AppStrings.hi.resx</c> — which does NOT fail on
    /// its own, because the lookup falls back to the English value with <c>ResourceNotFound</c>
    /// false and simply renders English inside a Hindi screen — fails here.
    /// </para>
    /// <para>
    /// The explicit list is KEPT rather than replaced: it is the only thing that would catch a key
    /// being dropped from a screen AND from the resources at the same time, which a scrape of that
    /// same screen cannot see.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void ResolvesEveryKeyTheRazorComponentsAskFor(string culture)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        var requested = RazorStringCoverage.KeysRequestedByComponents();
        Assert.True(
            requested.Count >= 400,
            $"Only {requested.Count} literal resource keys were found across the components. " +
            "REQ-UI-050 localized six full pages plus the shell, so the scrape is reading the " +
            "wrong tree and every assertion below would be vacuous.");

        // The culture's OWN key set: see ResolvesEveryKeyTheShippedScreensAskFor for why the
        // parent cultures are excluded for anything but English.
        var own = KeysFor(includeParentCultures: culture == SupportedLanguages.DefaultCulture);

        var missing = requested.Where(key => !own.Contains(key)).ToArray();
        Assert.True(
            missing.Length == 0,
            $"{missing.Length} key(s) are named by a razor component but missing from the {culture} " +
            $"resources, so those screens render English (or the key name) in a {culture} window: " +
            string.Join(", ", missing.Take(25)));

        foreach (var key in requested)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(localizer[key].Value),
                $"{key} resolves to an empty string in {culture}.");
        }
    }

    /// <summary>
    /// Every accent in the palette has a translated name. The swatch row's only accessible name is
    /// this string (REQ-NFR-005), so adding a sixth accent without a resource key would ship a
    /// colour button that screen readers announce as "AccentTeal".
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void NamesEveryAccentInEveryLanguage(string culture)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        foreach (var accent in AccentPalette.All)
        {
            // REQ-UI-055: the key travels ON the accent now instead of being rebuilt from the
            // persisted identifier, so this asserts the real contract rather than a re-derivation.
            var key = accent.DisplayNameKey;
            var value = localizer[key];

            Assert.False(
                value.ResourceNotFound,
                $"Accent '{accent.Key}' has no {key} entry in the {culture} resources.");
        }
    }

    /// <summary>
    /// The picker offers exactly the languages BRD-91 NAMES: English and Hindi.
    /// </summary>
    /// <remarks>
    /// Asserted as an exact sequence rather than as a count, which is the point of the 2026-07-29
    /// amendment. The requirement used to say "en plus at least 2 locales" and named none, so a
    /// count-based assertion stayed green while the app shipped German and French — two languages
    /// nobody had chosen against an Indian audience. Naming them here means a future locale change
    /// has to be a deliberate edit to a test that cites the requirement.
    /// </remarks>
    [Fact]
    public void OffersEnglishAndHindi()
    {
        Assert.Equal(2, SupportedLanguages.All.Count);
        Assert.Equal("en", SupportedLanguages.Default.Culture);
        Assert.Equal(["en", "hi"], SupportedLanguages.All.Select(language => language.Culture));
    }

    /// <summary>Each language names itself in itself, which is what the picker shows.</summary>
    [Fact]
    public void NamesEachLanguageInItself()
    {
        Assert.Equal("English", SupportedLanguages.Resolve("en").NativeName);
        Assert.Equal("हिन्दी", SupportedLanguages.Resolve("hi").NativeName);
    }

    /// <summary>
    /// Every theme option has a translated name in every shipped language. The radio labels are
    /// looked up from ModeOptions at runtime, so they never appear as literals anywhere a key-set
    /// comparison could see them (same reason as NamesEveryAccentInEveryLanguage).
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void NamesEveryThemeOptionInEveryLanguage(string culture)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        foreach (var mode in Enum.GetValues<ThemeMode>())
        {
            var value = localizer["Theme" + mode];

            Assert.False(
                value.ResourceNotFound,
                $"Theme option '{mode}' has no Theme{mode} entry in the {culture} resources.");
        }
    }

    /// <summary>
    /// German and French are WITHDRAWN, and asking for either lands on English.
    /// </summary>
    /// <param name="culture">A culture the product no longer ships.</param>
    /// <remarks>
    /// BRD-91 said "en plus at least 2 locales" and named none, so an earlier build shipped de and
    /// fr against no recorded decision; the owner named the set as en + hi on 2026-07-29. Deleting
    /// two .resx files leaves no failing test behind — the lookups simply fall back to English and
    /// everything stays green — so the removal is asserted here rather than assumed. IsSupported is
    /// the load-bearing half: it is what the picker uses, and it must now say no.
    /// </remarks>
    [Theory]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("de-AT")]
    [InlineData("fr-CA")]
    public void WithdrawnLocalesAreGone(string culture)
    {
        Assert.False(SupportedLanguages.IsSupported(culture));
        Assert.Equal("en", SupportedLanguages.Resolve(culture).Culture);
        Assert.DoesNotContain(SupportedLanguages.All, language => language.Culture == culture);

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        Assert.Equal("Theme", localizer["ThemeLabel"].Value);
    }

    /// <summary>
    /// Every Hindi string is non-empty and actually written in Devanagari.
    /// </summary>
    /// <remarks>
    /// ⚠ This proves the PLUMBING, not the GLYPHS. It asserts that the resource set loads and that
    /// its values carry Devanagari codepoints (U+0900–U+097F); it says nothing about whether the
    /// BlazorWebView has a font that can draw them, and a missing glyph renders as a tofu box on a
    /// screen this test never sees. The font-fallback check is a screenshot review, not a unit test
    /// — see REQ-UI-050.
    ///
    /// Latin runs are expected INSIDE the values and are not a failure: product and protocol nouns
    /// stay in Latin script on purpose ("Qdrant एडमिन", "LLM सेटिंग्स"), so the assertion is that
    /// each value contains Devanagari, not that it contains only Devanagari.
    /// </remarks>
    [Fact]
    public void EveryHindiStringIsWrittenInDevanagari()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hi");

        var strings = localizer.GetAllStrings(includeParentCultures: false).ToArray();
        Assert.NotEmpty(strings);

        foreach (var entry in strings)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(entry.Value),
                $"{entry.Name} is empty in the Hindi resources.");
            Assert.True(
                entry.Value.Any(character => character is >= '\u0900' and <= '\u097F'),
                $"{entry.Name} carries no Devanagari at all in the Hindi resources: '{entry.Value}'.");
        }
    }

    /// <summary>
    /// A term the project has SETTLED on is spelled that one way everywhere in the Hindi resources.
    /// </summary>
    /// <param name="rejected">The spelling that must not appear in any Hindi value.</param>
    /// <param name="settled">The spelling that replaced it.</param>
    /// <param name="decision">Where the decision was taken, quoted in the failure message.</param>
    /// <remarks>
    /// <para>
    /// Every other test here checks a value against ITSELF — is it Devanagari, does it keep its
    /// placeholders, is it not a copy of the English. None of them can see that two values
    /// translate the SAME word two different ways, because each is individually valid. That is a
    /// real defect and it is invisible: REQ-UI-050's tranche 2 shipped "ingestion" as both
    /// इंजेशन and इनजेशन, and "workspace" as both वर्कस्पेस and a bare Latin <c>workspace</c>, in
    /// screens a user moves between in one session.
    /// </para>
    /// <para>
    /// The failure mode this guards is drift, not ignorance: with well over a thousand keys and
    /// several clusters writing UI at once, nobody re-reads the register before translating a
    /// common word, so a variant re-enters months after it was cleaned up. Keeping the settled
    /// terms in a table that FAILS makes the decision durable instead of a one-off tidy-up, and
    /// makes extending it the obvious way to record the next one.
    /// </para>
    /// <para>
    /// This is deliberately a small, explicit list of terms somebody actually argued about. It is
    /// not a general Hindi style checker, and it must not grow into one — a rule nobody can defend
    /// in review is a rule that gets suppressed rather than followed.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// The four terms below were each settled by looking at what the resources ALREADY did and
    /// making the minority conform, not by preference:
    /// <list type="bullet">
    /// <item>ingestion — इनजेशन (2 uses) beat इंजेशन (1).</item>
    /// <item>workspace — वर्कस्पेस (56) beat a bare Latin <c>workspace</c> (5, all in MCP strings
    /// that also left <c>store</c> and <c>credentials</c> in Latin).</item>
    /// <item>status — स्टेटस (6) beat स्थिति (0 shipped; it reached two tranche-3 drafts because
    /// this pass's own hand-off note got it wrong).</item>
    /// <item>actions — क्रियाएँ (6 uses across Agents/Support/Automations) beat कार्रवाई (1).</item>
    /// <item>provider — प्रोवाइडर. This is the one that went AGAINST the count: the resources had
    /// प्रदाता 4 times and प्रोवाइडर never. It was still the outlier, because every other
    /// technical noun in this register is the loanword a Hindi-speaking software user actually
    /// says — वर्कस्पेस, कनेक्टर, एजेंट, टोकन, स्टोरेज, बैकअप, स्टेटस — and प्रदाता is the lone
    /// Sanskritic formal term among them. The 4 were normalised rather than the 9.</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData("इंजेशन", "इनजेशन", "REQ-UI-050 tranche 3, 2026-08-01")]
    [InlineData("workspace", "वर्कस्पेस", "REQ-UI-050 tranche 3, 2026-08-01")]
    [InlineData("Workspace", "वर्कस्पेस", "REQ-UI-050 tranche 3, 2026-08-01")]
    [InlineData("स्थिति", "स्टेटस", "REQ-UI-050 tranche 3, 2026-08-01")]
    [InlineData("प्रदाता", "प्रोवाइडर", "REQ-UI-050 tranche 3, 2026-08-01")]
    [InlineData("कार्रवाइ", "क्रियाएँ", "REQ-UI-050 tranche 3, 2026-08-01")]
    public void SettledTermsAreSpelledTheSameWayEverywhere(
        string rejected, string settled, string decision)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hi");

        var offenders = localizer.GetAllStrings(includeParentCultures: false)
            .Where(entry => entry.Value.Contains(rejected, StringComparison.Ordinal))
            .Select(entry => $"{entry.Name}: '{entry.Value}'")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{offenders.Length} Hindi value(s) still write '{rejected}' where this project has " +
            $"settled on '{settled}' ({decision}). One term, one spelling — a user who meets both " +
            "in one session cannot tell they mean the same thing:" + Environment.NewLine + "  " +
            string.Join(Environment.NewLine + "  ", offenders));
    }

    /// <summary>
    /// A composite-format string keeps its placeholder in Hindi, so the value it names still lands.
    /// </summary>
    /// <remarks>
    /// A translator dropping "{0}" is invisible in a key-set comparison and in a
    /// not-a-copy-of-the-English check, and it silently deletes the workspace name from the
    /// breadcrumb or the version number from the update toast.
    /// </remarks>
    [Fact]
    public void FormattedStringsKeepTheirPlaceholders()
    {
        string[] formatted =
        [
            "ShellUpdateAvailableMessage", "ShellBreadcrumbWorkspace",
            "UpgradeTitle", "UpgradeDescription", "UpgradeRequiresLicense"
        ];

        foreach (var culture in new[] { "en", "hi" })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

            foreach (var key in formatted)
            {
                Assert.Contains("{0}", localizer[key].Value, StringComparison.Ordinal);

                var formattedValue = localizer[key, "Sample"].Value;
                Assert.Contains("Sample", formattedValue, StringComparison.Ordinal);
                Assert.DoesNotContain("{0}", formattedValue, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// EVERY composite-format string keeps EVERY placeholder it has in English, in every language.
    /// </summary>
    /// <remarks>
    /// <see cref="FormattedStringsKeepTheirPlaceholders"/> names five strings and checks them
    /// thoroughly, including that formatting actually substitutes. This is the other half: it makes
    /// no list at all, so a formatted string added by a later tranche is covered the day it lands.
    /// REQ-UI-050's six-page tranche added enough of them that a named list would have gone stale
    /// within the same pass, and a dropped <c>{0}</c> is invisible to every other check here — the
    /// key sets still match, the Hindi is still Devanagari, it is still not a copy of the English,
    /// and the screen simply loses the workspace name or the file count it was meant to say.
    /// </remarks>
    [Fact]
    public void EveryPlaceholderSurvivesTranslation()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        var english = localizer.GetAllStrings(includeParentCultures: true)
            .ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);

        foreach (var culture in new[] { "hi" })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

            foreach (var entry in localizer.GetAllStrings(includeParentCultures: false))
            {
                if (!english.TryGetValue(entry.Name, out var source))
                {
                    continue;
                }

                var wanted = Placeholders(source);
                if (wanted.Count == 0)
                {
                    continue;
                }

                Assert.True(
                    wanted.SetEquals(Placeholders(entry.Value)),
                    $"{entry.Name} has placeholders {string.Join(", ", wanted.Order())} in English " +
                    $"but {string.Join(", ", Placeholders(entry.Value).Order())} in {culture}, so " +
                    $"the {culture} screen silently drops whatever that value was: '{entry.Value}'.");
            }
        }
    }

    /// <summary>Gets the indexed placeholders a composite-format string uses.</summary>
    /// <param name="value">The resource value.</param>
    /// <returns>The distinct placeholder indexes, e.g. <c>{0}</c> and <c>{1}</c>.</returns>
    private static HashSet<string> Placeholders(string value) =>
        System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+(?::[^}]*)?\}")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>An unknown culture resolves to English rather than throwing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ja-JP")]
    public void ResolvesAnUnknownCultureToEnglish(string? culture) =>
        Assert.Equal(SupportedLanguages.Default, SupportedLanguages.Resolve(culture));

    /// <summary>A regional variant of a shipped language resolves to that language.</summary>
    [Fact]
    public void ResolvesARegionalVariantToItsLanguage() =>
        Assert.Equal("hi", SupportedLanguages.Resolve("hi-IN").Culture);

    /// <summary><c>IsSupported</c> distinguishes "understood" from "rendered", unlike Resolve.</summary>
    [Fact]
    public void ReportsWhetherACultureIsShipped()
    {
        Assert.True(SupportedLanguages.IsSupported("hi-IN"));
        Assert.False(SupportedLanguages.IsSupported("ja-JP"));
        Assert.False(SupportedLanguages.IsSupported(null));
    }

    /// <summary>A stored choice wins over the machine's own language.</summary>
    [Fact]
    public async Task StoredChoiceOverridesTheOperatingSystem()
    {
        var settings = new FakeInstanceSettings();
        settings.Seed(LanguageStore.LanguageKey, "hi");
        var store = new LanguageStore(settings, () => "en-GB");

        var language = await store.LoadAsync();

        Assert.Equal("hi", language.Culture);
    }

    /// <summary>
    /// With no stored choice, a machine running a shipped language gets it — and the row stays
    /// EMPTY, so a machine later switched to another language still follows.
    /// </summary>
    [Fact]
    public async Task FollowsTheOperatingSystemUntilAChoiceIsMade()
    {
        var settings = new FakeInstanceSettings();
        var store = new LanguageStore(settings, () => "hi-IN");

        var language = await store.LoadAsync();

        Assert.Equal("hi", language.Culture);
        Assert.Empty(settings.Written);
    }

    /// <summary>A machine running a language TechieDesk does not ship gets English.</summary>
    [Fact]
    public async Task FallsBackToEnglishForAnUnshippedOperatingSystemLanguage()
    {
        var store = new LanguageStore(new FakeInstanceSettings(), () => "ja-JP");

        Assert.Equal("en", (await store.LoadAsync()).Culture);
    }

    /// <summary>A culture lookup that throws must not stop the app choosing a language.</summary>
    [Fact]
    public async Task SurvivesAFailingSystemCultureLookup()
    {
        var store = new LanguageStore(
            new FakeInstanceSettings(), () => throw new InvalidOperationException("no culture"));

        Assert.Equal("en", (await store.LoadAsync()).Culture);
    }

    /// <summary>A chosen language round-trips.</summary>
    [Fact]
    public async Task RoundTripsTheChoice()
    {
        var settings = new FakeInstanceSettings();
        var store = new LanguageStore(settings, () => "en-GB");

        await store.SaveAsync(SupportedLanguages.Resolve("hi"));

        Assert.Equal("hi", (await store.LoadAsync()).Culture);
    }

    /// <summary>
    /// Applying a language sets the FORMATTING culture as well as the UI culture. A Hindi UI that
    /// prints American dates is half-localized.
    /// </summary>
    [Fact]
    public void AppliesFormattingAndUiCultureTogether()
    {
        AppCulture.Apply(SupportedLanguages.Resolve("hi"));

        Assert.Equal("hi", CultureInfo.CurrentUICulture.Name);
        Assert.Equal("hi", CultureInfo.CurrentCulture.Name);
        Assert.Equal("hi", CultureInfo.DefaultThreadCurrentUICulture?.Name);
        Assert.Equal("hi", CultureInfo.DefaultThreadCurrentCulture?.Name);
    }

    /// <summary>Restores the culture so these tests cannot leak into the rest of the run.</summary>
    public void Dispose()
    {
        CultureInfo.CurrentUICulture = originalCulture;
        CultureInfo.CurrentCulture = originalCulture;
        CultureInfo.DefaultThreadCurrentCulture = null;
        CultureInfo.DefaultThreadCurrentUICulture = null;
        services.Dispose();
    }

    /// <summary>
    /// Every resource key named by a shipped razor component, grouped by the file that asks for it.
    /// Sources: Components/Layout/MainLayout.razor, Components/Shared/AppearancePanel.razor,
    /// BrandingPanel.razor, LanguagePicker.razor, ThemeToggle.razor, UpgradePrompt.razor and
    /// Components/Pages/AdminSettings.razor.
    /// </summary>
    private static readonly string[] KeysUsedByShippedScreens =
    [
        // AppearancePanel (REQ-UI-038). The theme and accent option keys are built at runtime from
        // AccentPalette/ThemeMode rather than written out, so they are covered by
        // NamesEveryAccentInEveryLanguage and NamesEveryThemeOptionInEveryLanguage instead.
        "AppearanceTitle", "AppearanceDescription", "ThemeLabel", "AccentLabel", "AccentDescription",

        // ThemeToggle (REQ-UI-038)
        "ThemeToggleLabel",

        // LanguagePicker (REQ-UI-039)
        "LanguageLabel", "LanguageDescription", "LanguageRestartNotice", "LanguageNotStored",

        // BrandingPanel (REQ-UI-037)
        "BrandingTitle", "BrandingDescription",
        "BrandingLogoLabel", "BrandingLogoHint", "BrandingLogoRemove",
        "BrandingDisplayNameLabel", "BrandingWelcomeLabel",
        "BrandingFooterLinksLabel", "BrandingFooterLinksHint", "BrandingPreviewLabel",
        "SaveChanges", "Saved", "SaveFailed", "RestoreDefaults",

        // AdminSettings — the screen that HOSTS the three panels (REQ-UI-037/038/039)
        "Saving",
        "AppSettingsTitle", "AppSettingsDescription",
        "AppSettingsTabDefaults", "AppSettingsTabBranding", "AppSettingsTabUpdates",
        "AppSettingsLoadFailedTitle", "AppSettingsLoading",

        // MainLayout — the shell, on every screen (REQ-UI-050)
        "ShellSkipToMain", "ShellSidebarToggle", "ShellUserMenu", "ShellThisMac",
        "ShellSwitchWorkspace", "ShellWorkspacesGroup", "ShellNoWorkspace", "ShellNoWorkspacesYet",
        "ShellNewWorkspaceAction", "ShellWorkspaceSettingsAction", "ShellPlanFree",
        "ShellLogOut", "ShellLogOutAllDevices", "ShellSignInActivate", "ShellSignInTooltip",
        "ShellTextIngestionTooltip", "ShellRagConfigTooltip",
        "ShellUpdateAvailableTitle", "ShellUpdateAvailableMessage", "ShellCreateWorkspaceFailed",
        "ShellBreadcrumbWorkspace",
        "NavGroupWorkspace", "NavGroupAccount", "NavGroupOperator", "NavGroupConsole",
        "NavChat", "NavDocuments", "NavConnectors", "NavAgents", "NavWorkspaceSettings",
        "NavProfile", "NavPricing", "NavBilling", "NavSupport", "NavSignIn",
        "NavEventLog", "NavAppSettings", "NavAutomations", "NavDataStorage", "NavBackupRestore",
        "NavUpdates", "NavQdrantAdmin", "NavLlmSettings", "NavTokenUsage", "NavTextIngestion",
        "NavLlmPlayground", "NavRagConfiguration",
        "PageHome", "PageRegister", "PagePasswordRecovery", "PageResetPassword",
        "PageFirstRunSetup", "PageFileIngestion", "PageWebSources",
        "PageAddConnector", "PageEditConnector",
        "NewWorkspaceTitle", "NewWorkspaceDescription", "NewWorkspaceNamePlaceholder",
        "Cancel", "Create", "ViewPlans",

        // MainLayout — the post-setup "no AI provider" hint (REQ-FN-050)
        "ShellNoProviderHint", "ShellNoProviderHintAction", "ShellNoProviderHintDismiss",

        // MainLayout — the background-startup strip (REQ-FN-049)
        "ShellStartupInitializing", "ShellStartupFailed", "ShellStartupFailedDismiss",

        // Setup — the first-run wizard's step indicator, Agents step and skip (REQ-FN-050)
        "SetupStepDefaults", "SetupStepAiProvider", "SetupStepAgents", "SetupStepAppManager",
        "SetupStepAdmin", "SetupStepWorkspace", "SetupStepIndicator",
        "SetupAgentsChecking", "SetupAgentsReadyTitle", "SetupAgentsReadyDescription",
        "SetupAgentsNoProviderNote", "SetupSkipLink",

        // UpgradePrompt — rendered INSIDE already-translated screens (REQ-UI-050)
        "UpgradeThisFeature", "UpgradeTitle", "UpgradeDescription", "UpgradeRequiresLicense"
    ];

    private HashSet<string> KeysFor(bool includeParentCultures) =>
        localizer.GetAllStrings(includeParentCultures)
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);
}
