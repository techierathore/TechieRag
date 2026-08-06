using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using TechieDesk.Resources;
using Xunit;
using Xunit.Abstractions;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// REQ-UI-052 (BRD-91): the localization ratchet for the ONE user-visible surface that is not
/// markup — the native macOS menu bar built by <c>MainPage.BuildMenuBar</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this file exists at all.</b> <c>StringCoverageTests.LocalizedFiles</c> can only guard
/// <c>.razor</c> files: <see cref="RazorStringCoverage"/> counts string sites in MARKUP, and the
/// menu bar is C#. So the menu bar was never in the denominator, never in the numerator, and stayed
/// hardcoded English through three localization tranches without one test going red — which is the
/// whole lesson of REQ-UI-052. An unguarded surface does not stay fixed; it silently re-grows.
/// </para>
/// <para>
/// <b>What can and cannot be guarded here.</b> The net10.0 test project cannot reference the MAUI
/// head (see <c>TechieDesk.Tests.csproj</c>), so <c>BuildMenuBar</c> cannot be CALLED and the menu
/// cannot be asserted as objects. What is available is the source file itself, read off disk the
/// same way <see cref="RazorStringCoverage"/> reads the components — anchored on
/// <c>TechieRag.slnx</c> so a wrong path THROWS rather than passing vacuously. That is weaker than
/// running the code, and it is stated plainly rather than dressed up: this proves the captions are
/// resource keys and that those keys resolve in both languages. It does NOT prove UIKit drew them.
/// Only the live smoke does that, which is why REQ-UI-052 also asks for one.
/// </para>
/// <para>
/// <b>Why a source scan is nonetheless a real ratchet.</b> Every caption is passed as a key, so the
/// scan cannot tell a key from a literal by SHAPE — but it does not have to. A key must exist in
/// <c>AppStrings.resx</c> AND <c>AppStrings.hi.resx</c> and must look like <c>Menu*</c>, and English
/// prose satisfies neither: adding <c>MenuItem("Print…", "P", …)</c> fails
/// <see cref="EveryMenuCaptionIsALocalizedResourceKey"/> on the change that adds it. The resource
/// namespace and the English language do not overlap, and that is what makes this hold.
/// </para>
/// </remarks>
public sealed class MenuBarLocalizationTests : IDisposable
{
    private readonly ServiceProvider services;
    private readonly IStringLocalizer<AppStrings> localizer;
    private readonly CultureInfo originalCulture = CultureInfo.CurrentUICulture;
    private readonly ITestOutputHelper output;

    /// <summary>Builds a localization container equivalent to the app's.</summary>
    /// <param name="output">The xunit output sink.</param>
    public MenuBarLocalizationTests(ITestOutputHelper output)
    {
        this.output = output;
        services = new ServiceCollection()
            .AddLogging()
            .AddLocalization()
            .BuildServiceProvider();

        localizer = services.GetRequiredService<IStringLocalizer<AppStrings>>();
    }

    /// <summary>A menu title: <c>new MenuBarItem { Text = Text("MenuFile") }</c>.</summary>
    private static readonly Regex MenuTitle = new(
        @"new\s+MenuBarItem\s*\{\s*Text\s*=\s*Text\(\s*""([^""]+)""\s*\)\s*\}");

    /// <summary>
    /// One menu item: its caption key, its accelerator (a quoted key or <c>null</c>), and the rest
    /// of the line, which is where <c>shift: true</c> would be.
    /// </summary>
    private static readonly Regex MenuEntry = new(
        @"MenuItem\(\s*""([^""]+)""\s*,\s*(?:key:\s*)?(null|""[^""]*"")\s*,([^\n]*)");

    /// <summary>Any string handed to the localizer helper, including the dialog copy.</summary>
    private static readonly Regex Lookup = new(@"\bText\(\s*""([^""]+)""");

    /// <summary>A resource key, as opposed to a sentence somebody forgot to translate.</summary>
    private static readonly Regex KeyShape = new(@"^Menu[A-Za-z0-9]+$");

    /// <summary>
    /// THE MENU BAR'S KEYBOARD CONTRACT, as REQ-UI-041 shipped it and REQ-UI-049 amended it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written out here because REQ-UI-052 changes every caption on this menu and an accelerator is
    /// the thing most likely to be lost while doing that — it sits in the same argument list, it is
    /// invisible in a screenshot, and nothing else would notice. A key equivalent is NOT UI copy: it
    /// is a platform contract a user has in muscle memory, identical in every language. ⌘, opens
    /// App Settings on a Hindi Mac exactly as it does on an English one.
    /// </para>
    /// <para>
    /// The modifier is deliberately absent from this table. <c>MenuItem</c> chooses Cmd on
    /// macOS and Ctrl on Windows from <c>OperatingSystem.IsWindows()</c>, so asserting a modifier
    /// here would assert the test host's platform rather than the product's behaviour.
    /// </para>
    /// </remarks>
    private static readonly (string Key, string? Accelerator, bool Shift)[] Accelerators =
    [
        ("MenuFileImportDocuments", "O", false),
        ("MenuFileImportFolder", "O", true),
        ("MenuFileRevealDataFolder", "R", true),
        ("MenuFileRevealLogsFolder", "L", true),
        ("MenuGoHome", "1", false),
        ("MenuGoChat", "2", false),
        ("MenuGoIngestion", "3", false),
        ("MenuGoTokenUsage", "4", false),
        ("MenuGoSettings", ",", false),
        ("MenuGoRagConfiguration", null, false),
        ("MenuGoLlmSettings", "L", false),
        ("MenuGoDataStorage", "D", false),
        ("MenuViewZoomIn", "+", false),
        ("MenuViewZoomOut", "-", false),
        ("MenuViewActualSize", "0", false),
        ("MenuHelpCheckForUpdates", null, false),
        ("MenuHelpVersionAndDataFolder", null, false),
        ("MenuHelpWhereIsMyData", null, false),
    ];

    /// <summary>
    /// Every caption on the native menu bar is a resource key that resolves in every shipped
    /// language — so no English literal can reach the menu bar.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// This is the ratchet. It covers the menu titles, every item under them, and the copy of the
    /// native dialogs those items raise (the file picker's title, the alert bodies, the about box),
    /// because a Hindi menu that opens an English alert is the same defect one click later.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryMenuCaptionIsALocalizedResourceKey(string culture)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        var keys = CaptionKeys();
        Assert.True(
            keys.Count >= 30,
            $"Only {keys.Count} menu captions were found in {MenuPagePath()}. The menu bar has four " +
            "menus, eighteen items and its dialog copy, so the scan is reading the wrong file or the " +
            "call shape has changed — and every assertion below would be vacuous.");

        // The culture's OWN key set: a key missing from hi.resx but present in the neutral set
        // resolves to the ENGLISH text with ResourceNotFound false, which is exactly the
        // half-translated menu this test exists to catch.
        var own = localizer.GetAllStrings(includeParentCultures: culture == "en")
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

        var literals = keys.Where(key => !KeyShape.IsMatch(key)).ToArray();
        Assert.True(
            literals.Length == 0,
            "These menu-bar captions are not resource keys, so they are English text on the one " +
            "user-visible surface the razor coverage counter cannot see. Give each a Menu* key in " +
            "AppStrings.resx AND AppStrings.hi.resx: " + string.Join(", ", literals));

        var missing = keys.Where(key => !own.Contains(key)).ToArray();
        Assert.True(
            missing.Length == 0,
            $"{missing.Length} menu-bar caption key(s) are missing from the {culture} resources, so " +
            $"the menu renders English (or the key name) in a {culture} window: " +
            string.Join(", ", missing));

        foreach (var key in keys)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(localizer[key].Value),
                $"{key} resolves to an empty string in {culture}, so that menu item has no caption.");
        }
    }

    /// <summary>
    /// The menu bar really is translated: every caption reads differently in Hindi than in English.
    /// </summary>
    /// <remarks>
    /// <see cref="EveryMenuCaptionIsALocalizedResourceKey"/> proves each key EXISTS in both sets.
    /// A key whose Hindi value was pasted from the English would satisfy that and still ship an
    /// English menu bar, which is precisely the state REQ-UI-052 found the app in. The equivalent
    /// check exists app-wide in <c>LocalizationTests.TranslationsAreNotCopiesOfTheEnglish</c>; it is
    /// repeated here on the menu keys alone so that a failure names the menu bar rather than
    /// arriving as one row among 1,869.
    /// </remarks>
    [Fact]
    public void TheMenuBarReadsDifferentlyInEachLanguage()
    {
        var keys = CaptionKeys();

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        var english = keys.ToDictionary(key => key, key => localizer[key].Value, StringComparer.Ordinal);

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hi");

        var untranslated = keys
            .Where(key => string.Equals(english[key], localizer[key].Value, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            untranslated.Length == 0,
            "These menu-bar captions are byte-for-byte their English source in Hindi, so the menu " +
            "bar is still English however many keys it now goes through: " +
            string.Join(", ", untranslated));

        output.WriteLine($"Menu bar: {keys.Count} caption keys, all translated.");
        foreach (var title in TitleKeys())
        {
            output.WriteLine($"  {title,-10} -> {localizer[title].Value}");
        }
    }

    /// <summary>The menu bar still offers exactly the four menus REQ-UI-041 shipped, in order.</summary>
    /// <remarks>
    /// Asserted as an ordered sequence, not a set: File / Go / View / Help is the order a macOS user
    /// reads left to right, and localizing the titles must not reorder them.
    /// </remarks>
    [Fact]
    public void TheMenuBarNamesTheFourMenusInOrder() =>
        Assert.Equal(["MenuFile", "MenuGo", "MenuView", "MenuHelp"], TitleKeys());

    /// <summary>
    /// Localizing the captions left every keyboard accelerator exactly where it was (REQ-UI-041 /
    /// REQ-UI-049).
    /// </summary>
    /// <remarks>
    /// ⌘, in particular: it is macOS's standard Settings shortcut and REQ-UI-049 deliberately moved
    /// it onto App Settings. Nothing about translating a caption may move it back.
    /// </remarks>
    [Fact]
    public void MenuAcceleratorsAreUnchangedByLocalization()
    {
        var source = File.ReadAllText(MenuPagePath());

        var found = MenuEntry.Matches(source)
            .Select(match => (
                Key: match.Groups[1].Value,
                Accelerator: match.Groups[2].Value == "null"
                    ? null
                    : match.Groups[2].Value.Trim('"'),
                Shift: match.Groups[3].Value.Contains("shift: true", StringComparison.Ordinal)))
            .ToArray();

        Assert.Equal(Accelerators, found);
    }

    /// <summary>
    /// The scan is really reading <c>MainPage.xaml.cs</c>, and the shapes it depends on are there.
    /// </summary>
    /// <remarks>
    /// The whole guard rests on a regex over a file located by a path walk. If either the file moves
    /// or the call shape changes, every assertion above quietly matches nothing and passes forever —
    /// the exact failure mode <see cref="RazorStringCoverage.FindComponentsRoot"/> was written to
    /// avoid, so it is answered the same way: locate by anchor, and assert a plausible count.
    /// </remarks>
    [Fact]
    public void TheScanReadsTheRealMenuBarSource()
    {
        var path = MenuPagePath();
        Assert.True(File.Exists(path), $"{path} does not exist.");

        var source = File.ReadAllText(path);
        Assert.Contains("BuildMenuBar", source, StringComparison.Ordinal);
        Assert.Equal(4, MenuTitle.Matches(source).Count);
        Assert.Equal(18, MenuEntry.Matches(source).Count);
    }

    /// <summary>Gets the resource keys of the four menu titles, in the order they are added.</summary>
    private static string[] TitleKeys() =>
        MenuTitle.Matches(File.ReadAllText(MenuPagePath()))
            .Select(match => match.Groups[1].Value)
            .ToArray();

    /// <summary>
    /// Gets every resource key the menu bar and its dialogs name, deduplicated and ordered.
    /// </summary>
    private static IReadOnlyList<string> CaptionKeys()
    {
        var source = File.ReadAllText(MenuPagePath());

        return MenuEntry.Matches(source).Select(match => match.Groups[1].Value)
            .Concat(Lookup.Matches(source).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Gets the absolute path of the head's menu-bar source.</summary>
    /// <remarks>
    /// Derived from <see cref="RazorStringCoverage.FindComponentsRoot"/> rather than re-deriving the
    /// repository root, so there is ONE anchor on <c>TechieRag.slnx</c> and one thing to fix if the
    /// head moves. That method throws when it cannot find the repository, which is what stops this
    /// whole class from degrading into a test that passes on an empty string.
    /// </remarks>
    private static string MenuPagePath() =>
        Path.GetFullPath(Path.Combine(
            RazorStringCoverage.FindComponentsRoot(), "..", "MainPage.xaml.cs"));

    /// <summary>Restores the culture so these tests cannot leak into the rest of the run.</summary>
    public void Dispose()
    {
        CultureInfo.CurrentUICulture = originalCulture;
        CultureInfo.CurrentCulture = originalCulture;
        services.Dispose();
    }
}
