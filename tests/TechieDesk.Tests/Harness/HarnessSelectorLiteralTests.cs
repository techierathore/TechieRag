using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using TechieDesk.Resources;
using Xunit;
using Xunit.Abstractions;

namespace TechieDesk.Tests.Harness;

/// <summary>
/// REQ-NFR-014 guard 2: no selector in the Appium harness is an English literal, so localizing a
/// surface can never again silently invalidate the harness that identifies it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three times, same pattern.</b> <c>nav.sidebar()</c> matched English sidebar captions until
/// REQ-UI-050 localized them and every one of 21 screens failed <c>nav link NOT FOUND</c>.
/// <c>nav.go_menu()</c> matched English menu captions until REQ-UI-052 localized the menu bar, which
/// would have stranded the sweep on <c>/login</c> with no way back into the shell. And
/// <c>run_sweep.CHROMELESS</c> identified <c>/login</c> by the literal
/// "Sign in to your TechieDesk instance" until the same requirement turned that string into
/// <c>LoginSubheading</c>. The harness README states the rule it learned and then says the fix is
/// "not yet written". This is that fix.
/// </para>
/// <para>
/// <b>DEFINITION — what "is an English literal" means for a selector.</b> A <i>selector</i> is a
/// string constant the harness matches against text the running app RENDERS (the <c>label</c> /
/// <c>title</c> / <c>value</c> of an accessibility node). A selector is <b>culture-invariant</b>
/// when it is one of exactly two things, each verified against a product artefact rather than
/// judged by eye:
/// </para>
/// <list type="number">
/// <item>a <b>resource key</b> that exists in <c>AppStrings.resx</c> — the harness resolves it
/// through <c>strings.py</c> at run time, so it renders whatever the app renders, in whatever
/// language; or</item>
/// <item>a <b>stable code handle</b> — an <c>AutomationId</c> that exists as an <c>id="…"</c> in
/// <c>MainLayout.razor</c>, which is the same string in every language and owes nothing to the
/// resource table (REQ-UI-053).</item>
/// </list>
/// <para>
/// A selector <b>is an English literal</b> when it is neither: it is displayed text frozen into the
/// harness. Operationally that is asserted two ways, because the two catch different moments:
/// </para>
/// <list type="bullet">
/// <item><see cref="EverySweepSelectorIsCultureInvariant"/> — a whitelist over the harness's
/// selector tables. Every value must be a resource key, a <c>key:</c> marker, or a known
/// <c>AutomationId</c>. Nothing else is admissible, whatever it looks like.</item>
/// <item><see cref="NoHarnessLiteralIsAStringTheProductDisplays"/> — the catch-all. Any string
/// constant anywhere in <c>tests/appium/*.py</c> that is byte-identical to an English value in
/// <c>AppStrings.resx</c> is, by definition, text the product shows and translation will change.
/// </item>
/// </list>
/// <para>
/// <b>Why the catch-all is resistant to false positives.</b> It does not guess whether a string
/// "looks English" — a word list or a prose heuristic would fire on <c>"XCUIElementTypeLink"</c>,
/// <c>"mac2"</c> and every diagnostic message in the file. It asks one factual question of the
/// product's own resource table: <i>is this exact string something the app displays?</i> Docstrings
/// (all the prose that documents these very failures) and f-strings (message templates, whose
/// fragments are sentence punctuation) are excluded structurally, not by taste. Measured over the
/// current harness the whole scan produces FIVE hits, all real, all classified below.
/// </para>
/// <para>
/// <b>Why it is also causally right, not merely convenient.</b> A literal that is not yet an
/// <c>AppStrings</c> value is a selector for a surface nobody has localized — it still works. The
/// moment somebody localizes that surface the string ENTERS <c>AppStrings</c>, and this test goes
/// red on that same change. Run against REQ-UI-052 it would have failed the commit that added
/// <c>LoginSubheading</c>, which is precisely where the third instance should have been caught.
/// </para>
/// </remarks>
public sealed class HarnessSelectorLiteralTests : IDisposable
{
    private readonly ServiceProvider services;
    private readonly IStringLocalizer<AppStrings> localizer;
    private readonly CultureInfo originalCulture = CultureInfo.CurrentUICulture;
    private readonly ITestOutputHelper output;

    /// <summary>Builds a localization container equivalent to the app's, pinned to English.</summary>
    /// <param name="output">The xunit output sink.</param>
    /// <remarks>
    /// English on purpose: the question this class asks is "is this literal a string the product
    /// SHOWS AN ENGLISH USER", and the neutral <c>AppStrings.resx</c> is that set.
    /// </remarks>
    public HarnessSelectorLiteralTests(ITestOutputHelper output)
    {
        this.output = output;
        services = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();
        localizer = services.GetRequiredService<IStringLocalizer<AppStrings>>();
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }

    /// <summary>
    /// The harness constants whose values ARE matched against what the app renders. Every value of
    /// each one has to be culture-invariant.
    /// </summary>
    private static readonly string[] SelectorTables =
    [
        "run_sweep.py:SIDEBAR",
        "run_sweep.py:CHROMELESS",
        "nav.py:NAV_IDS",
        "nav.py:GO_MENU_KEY",
        "menu_check.py:STANDARD_TITLES",
    ];

    /// <summary>
    /// The harness's other string-bearing constants — endpoints, paths, file names, element type
    /// names, regexes. None of them is compared with app-rendered text.
    /// </summary>
    /// <remarks>
    /// Listed rather than inferred so that a NEW constant is neither silently treated as a selector
    /// nor silently ignored: <see cref="EveryStringBearingHarnessConstantIsClassified"/> fails until
    /// somebody puts it in one list or the other. That is the same anti-incompleteness argument as
    /// guard 1 — a registry nobody is forced to update is a registry that goes stale.
    /// </remarks>
    private static readonly string[] NonSelectorConstants =
    [
        "drv.py:HUB", "drv.py:APP_PATH", "drv.py:BUNDLE", "drv.py:STATE",
        "menu_check.py:MENU_SOURCE", "menu_check.py:MENU_TITLE", "menu_check.py:MENU_ENTRY",
        "run_sweep.py:RESULTS", "run_sweep.py:DATA_DIR", "run_sweep.py:LOCKS", "run_sweep.py:WDA",
        "strings.py:RESX_DIR", "strings.py:APP_DB", "strings.py:NEUTRAL",
        "strings.py:LANG_KEY", "strings.py:DEFAULT_LANG",
        "sweep.py:OUT", "sweep.py:INTERACTIVE", "sweep.py:TEXTY",
        "update_devguides.py:D", "update_devguides.py:BASE", "update_devguides.py:OBS",
        "update_devguides.py:HDR", "update_devguides.py:FILES",
    ];

    /// <summary>
    /// The only strings the harness may hold that the product also displays, each with the reason it
    /// is not a localization hazard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately an EXACT-MATCH list of whole values, deliberately tiny, and every entry must
    /// still be present in the harness — the same discipline
    /// <c>RazorStringCoverage.Untranslatable</c> is held to, for the same reason: this is the one
    /// place the guard takes somebody's word for it, so adding an entry has to be a decision
    /// somebody defends rather than a way to make a red build go green.
    /// </para>
    /// <para>
    /// The reviewer's question for a new entry is always: <i>if the app's language changes, does
    /// this stop matching?</i> If yes it is a defect and does not belong here, whatever the
    /// convenience.
    /// </para>
    /// </remarks>
    private static readonly (string FileName, string Literal, string Reason)[] AllowedEnglishLiterals =
    [
        ("menu_check.py", "File",
            "menu_check.STANDARD_TITLES. macOS owns these five stock menus; their titles follow the " +
            "SYSTEM language, not the app's AppearanceLanguage, so AppStrings is the wrong table to " +
            "resolve them from. The truly invariant handle is UIMenuIdentifier, which the " +
            "accessibility tree does not expose. Residual risk, stated rather than hidden: on a " +
            "non-English macOS this misses the stock menu — but it then reports MENU MISSING LOUDLY " +
            "rather than quietly ceasing to assert, and loud-versus-silent is the whole distinction " +
            "this guard is about. Verified 2026-08-02 on a Hindi app: the bar carried File/Edit/View/" +
            "Window/Help alongside the app's own फ़ाइल/जाएँ/दृश्य/मदद, and menu_check passed."),
        ("menu_check.py", "Edit", "as File above — a macOS-owned stock menu title, same reasoning."),
        ("menu_check.py", "View", "as File above — a macOS-owned stock menu title, same reasoning."),
        ("menu_check.py", "Window", "as File above — a macOS-owned stock menu title, same reasoning."),
        ("menu_check.py", "Help", "as File above — a macOS-owned stock menu title, same reasoning."),
        ("drv.py", "new",
            "drv.py's __main__ dispatch: `elif cmd == \"new\"` compares a COMMAND-LINE subcommand " +
            "against sys.argv[1]. It never touches the accessibility tree. It collides with the " +
            "BackupNewBadge caption by pure coincidence of a three-letter word."),
    ];

    /// <summary>
    /// Every value in every selector table is a resource key, a <c>key:</c> marker, or a real
    /// <c>AutomationId</c> — never displayed text.
    /// </summary>
    /// <remarks>
    /// The positive form of the rule, and the stronger of the two: it does not ask whether a value
    /// looks like English, it requires the value to be provably resolvable at run time. A literal
    /// caption cannot satisfy it even before anybody translates the screen, so this catches the
    /// hazard at the moment the selector is written rather than at the moment it breaks.
    /// </remarks>
    [Fact]
    public void EverySweepSelectorIsCultureInvariant()
    {
        var resourceKeys = ResourceKeys();
        var automationIds = HarnessSource.DeclaredSidebarButtons()
            .Select(button => button.AutomationId)
            .ToHashSet(StringComparer.Ordinal);

        var selectors = new List<(string Where, string Value)>();
        selectors.AddRange(HarnessSource.SweepSidebarTable()
            .Select(row => ($"run_sweep.SIDEBAR[{row.Slug}]", row.Key)));
        selectors.AddRange(HarnessSource.ChromelessMarkers()
            .Select(marker => ($"run_sweep.CHROMELESS[{marker.Slug}]", marker.Marker)));
        selectors.AddRange(HarnessSource.NavIdTable()
            .Select(row => ($"nav.NAV_IDS[{row.Key}]", row.AutomationId)));
        selectors.Add(("nav.GO_MENU_KEY", HarnessSource.ScalarConstant("nav.py", "GO_MENU_KEY")));
        selectors.AddRange(HarnessSource.StandardMenuTitles()
            .Select(title => ($"menu_check.STANDARD_TITLES[{title.Declared}]", title.Stock)));

        Assert.True(selectors.Count >= 45, $"only {selectors.Count} selectors were read — the scan is vacuous");

        // STANDARD_TITLES is the one table that CANNOT meet the bar — see the exemption's reason —
        // so it is filtered through the same registry rather than left unexamined. A sixth stock
        // title added there fails until somebody defends it.
        var exempt = AllowedEnglishLiterals.Select(entry => entry.Literal).ToHashSet(StringComparer.Ordinal);

        var literals = selectors
            .Where(selector => !IsCultureInvariant(selector.Value, resourceKeys, automationIds))
            .Where(selector => !exempt.Contains(selector.Value))
            .Select(selector => $"{selector.Where} = '{selector.Value}'")
            .ToArray();

        Assert.True(
            literals.Length == 0,
            $"{literals.Length} harness selector(s) are English literals rather than something the " +
            "harness can resolve per language. Each must be a resource key present in " +
            "AppStrings.resx (resolved through strings.py), a 'key:<ResourceKey>' marker, or an " +
            "AutomationId that MainLayout.razor really renders. Localizing the surface these name " +
            "will silently stop them matching — it has happened three times: " +
            string.Join("; ", literals));

        output.WriteLine($"{selectors.Count} selectors checked, all culture-invariant.");
    }

    /// <summary>
    /// No string constant anywhere in the harness is byte-identical to something the product
    /// displays in English.
    /// </summary>
    /// <remarks>
    /// The catch-all behind the table whitelist, and the one that would have caught the third
    /// instance. It covers function-local selectors too — <c>recover_to_app()</c> used to try the
    /// captions <c>("Chat", "Home", "Workspace")</c> from inside a function body, where no table
    /// scan could see them.
    /// </remarks>
    [Fact]
    public void NoHarnessLiteralIsAStringTheProductDisplays()
    {
        var displayed = DisplayedEnglishValues();
        var exempt = AllowedEnglishLiterals
            .Select(entry => $"{entry.FileName}|{entry.Literal}")
            .ToHashSet(StringComparer.Ordinal);

        var scanned = 0;
        var offenders = new List<string>();

        foreach (var path in HarnessSource.HarnessFiles())
        {
            foreach (var literal in HarnessSource.StringLiterals(path))
            {
                // Punctuation-only literals are excluded because the resource table contains some
                // ("." is StorageSizesNoteEnd) and nothing identifies a screen by a full stop. Two
                // ASCII letters is the floor; every real caption clears it, so this cannot hide a
                // selector — it only stops the scan getting noisier as AppStrings grows.
                if (literal.IsTripleQuoted || literal.IsFormatString ||
                    literal.Value.Count(char.IsAsciiLetter) < 2)
                {
                    continue;
                }

                scanned++;
                if (!displayed.TryGetValue(literal.Value, out var keys) ||
                    exempt.Contains($"{literal.FileName}|{literal.Value}"))
                {
                    continue;
                }

                offenders.Add(
                    $"{literal.FileName}:{literal.Line} '{literal.Value}' == AppStrings[{string.Join("/", keys)}]");
            }
        }

        Assert.True(scanned >= 200, $"only {scanned} harness string constants were scanned — the scan is vacuous");

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} harness literal(s) are byte-identical to text the product DISPLAYS, so " +
            "they are English captions frozen into the harness and translating that surface will stop " +
            "them matching. Replace each with the resource key (resolve it via strings.all_candidates) " +
            "or with the element's AutomationId: " + string.Join("; ", offenders));

        output.WriteLine($"{scanned} harness string constants scanned against {displayed.Count} displayed English values.");
    }

    /// <summary>
    /// Every string-bearing constant in the harness is classified as a selector table or as not one.
    /// </summary>
    /// <remarks>
    /// The anti-incompleteness ratchet for this guard, exactly parallel to guard 1's. Without it a
    /// cluster could add a new table of English captions and both tests above would pass — they
    /// would simply never look at it, which is the "silently incomplete harness" failure wearing a
    /// different hat.
    /// </remarks>
    [Fact]
    public void EveryStringBearingHarnessConstantIsClassified()
    {
        var known = SelectorTables.Concat(NonSelectorConstants).ToHashSet(StringComparer.Ordinal);
        var found = new List<string>();

        foreach (var path in HarnessSource.HarnessFiles())
        {
            var fileName = Path.GetFileName(path);
            found.AddRange(HarnessSource.StringBearingConstants(fileName)
                .Select(name => $"{fileName}:{name}"));
        }

        Assert.True(found.Count >= 25, $"only {found.Count} harness constants were found — the scan is vacuous");

        var unclassified = found.Where(entry => !known.Contains(entry)).ToArray();
        Assert.True(
            unclassified.Length == 0,
            $"{unclassified.Length} harness constant(s) hold string values but are in neither " +
            "SelectorTables nor NonSelectorConstants. Decide which: if its values are matched " +
            "against what the app RENDERS it is a selector table and every value must be a resource " +
            "key or an AutomationId; otherwise list it as a non-selector. Leaving it out means no " +
            "guard ever looks at it: " + string.Join(", ", unclassified));

        var retired = known.Where(entry => !found.Contains(entry)).ToArray();
        Assert.True(
            retired.Length == 0,
            $"{retired.Length} classified constant(s) no longer exist in the harness — delete the " +
            "stale registry entries so the classification keeps meaning something: " +
            string.Join(", ", retired));
    }

    /// <summary>The English-literal exemption list stays tiny, and every entry is still in use.</summary>
    /// <remarks>
    /// An exemption list that can grow without limit is a way of turning this guard off one row at a
    /// time, and one that keeps dead rows stops describing the harness. Both are asserted.
    /// </remarks>
    [Fact]
    public void TheEnglishLiteralExemptionsStaySmallAndInUse()
    {
        Assert.True(
            AllowedEnglishLiterals.Length <= 8,
            $"{AllowedEnglishLiterals.Length} exemptions. This list is the guard's only escape hatch; " +
            "past a handful it IS the guard. Fix the selector instead.");

        Assert.All(AllowedEnglishLiterals, entry => Assert.True(
            entry.Reason.Length >= 40,
            $"the exemption for '{entry.Literal}' has no real justification written against it."));

        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in HarnessSource.HarnessFiles())
        {
            foreach (var literal in HarnessSource.StringLiterals(path))
            {
                present.Add($"{literal.FileName}|{literal.Value}");
            }
        }

        var dead = AllowedEnglishLiterals
            .Where(entry => !present.Contains($"{entry.FileName}|{entry.Literal}"))
            .Select(entry => $"{entry.FileName}:'{entry.Literal}'")
            .ToArray();

        Assert.True(
            dead.Length == 0,
            $"{dead.Length} exemption(s) name a literal the harness no longer contains. Delete them: " +
            string.Join(", ", dead));
    }

    /// <summary>Gets every resource key the product ships.</summary>
    private HashSet<string> ResourceKeys() =>
        localizer.GetAllStrings(includeParentCultures: true)
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Gets every English string the product displays, mapped to the keys that produce it.</summary>
    private Dictionary<string, List<string>> DisplayedEnglishValues()
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var entry in localizer.GetAllStrings(includeParentCultures: true))
        {
            var value = entry.Value.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (!values.TryGetValue(value, out var keys))
            {
                values[value] = keys = [];
            }

            keys.Add(entry.Name);
        }

        return values;
    }

    /// <summary>Decides whether one selector can be resolved without knowing the app's language.</summary>
    /// <param name="value">The selector as the harness writes it.</param>
    /// <param name="resourceKeys">Every key <c>AppStrings.resx</c> defines.</param>
    /// <param name="automationIds">Every <c>id</c> <c>MainLayout.razor</c> renders on a sidebar link.</param>
    /// <returns>True when it is a resource key, a <c>key:</c> marker, or a known AutomationId.</returns>
    private static bool IsCultureInvariant(
        string value,
        IReadOnlySet<string> resourceKeys,
        IReadOnlySet<string> automationIds) =>
        value.StartsWith("key:", StringComparison.Ordinal)
            ? resourceKeys.Contains(value[4..])
            : resourceKeys.Contains(value) || automationIds.Contains(value);

    /// <summary>Restores the culture so this class cannot leak into the rest of the run.</summary>
    public void Dispose()
    {
        CultureInfo.CurrentUICulture = originalCulture;
        services.Dispose();
    }
}
