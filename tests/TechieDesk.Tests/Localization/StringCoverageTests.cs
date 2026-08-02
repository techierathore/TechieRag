using Xunit;
using Xunit.Abstractions;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// REQ-UI-050 (BRD-91): the app-wide localization coverage ratchet.
/// </summary>
/// <remarks>
/// <para>
/// REQ-UI-039 shipped a working .resx pipeline and REQ-UI-039's verification then measured what it
/// actually covered: 45 of 1,928 user-visible string sites, 2.3%. A number nobody can reproduce is a
/// number that quietly stops being true, so the counter that produced it lives in
/// <see cref="RazorStringCoverage"/> and runs here.
/// </para>
/// <para>
/// The RATCHET is on the ABSOLUTE count, not on the percentage, and that is the important design
/// choice. Four build clusters write UI concurrently, and every English literal any of them adds
/// grows the denominator — so a percentage floor set at today's value would go red because someone
/// ELSE shipped a feature, which trains people to raise the threshold's exemption list rather than
/// to translate anything. The absolute floor can only fall if localization is REMOVED, which is the
/// regression worth failing a build over. The percentage is asserted too, with a deliberate margin,
/// and is reported on every run so the trend is visible.
/// </para>
/// </remarks>
public sealed class StringCoverageTests
{
    private readonly ITestOutputHelper output;

    /// <summary>Captures the xunit output sink so every run prints the current coverage.</summary>
    /// <param name="output">The xunit output sink.</param>
    public StringCoverageTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// The number of localized sites achieved by REQ-UI-050. Raise it when coverage is extended;
    /// never lower it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 45 on 2026-07-29 (REQ-UI-039's measured starting point), 117 later the same day, 942 on
    /// 2026-07-31 when the six largest pages were converted, 1,709 on 2026-08-01 when the next ten
    /// were, and 2,280 on 2026-08-01 when the fourth tranche finished the tree. The numerator's
    /// DEFINITION has never changed, so those figures are directly comparable even though the
    /// 2026-07-31 pass corrected three false-positive classes in the denominator and the 2026-08-01
    /// pass WIDENED it — see the remarks on <see cref="RazorStringCoverage"/>.
    /// </para>
    /// <para>
    /// 2,280 is every user-visible markup site the counter can see, so this floor now says "nothing
    /// may be taken out" rather than "keep going". Coverage growing past it means new UI arrived
    /// localized, which is the policy.
    /// </para>
    /// </remarks>
    private const int LocalizedSiteFloor = 2280;

    /// <summary>
    /// Coverage reached 100.0% (2,280 of 2,280) on 2026-08-01. The floor is set well under it ONLY
    /// to absorb the denominator growth caused by concurrent English UI — see the class remarks. It
    /// is not headroom to spend.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised from 76.0 with the fourth tranche. The margin is still wide, and deliberately so: the
    /// denominator was observed moving 2,036 -> 2,113 -> 2,133 -> 2,274 -> 2,344 across three
    /// tranches, purely from other clusters shipping UI, and a margin narrower than the measured
    /// drift would fail this build for somebody else's feature — the exact incentive the absolute
    /// floor above exists to avoid. Ten points of room at this denominator is roughly 250 new
    /// English sites; the absolute floor, not this number, is what catches localization being
    /// removed.
    /// </para>
    /// <para>
    /// It is NOT set to 100 on purpose. A percentage floor at the current value goes red the day
    /// somebody else ships a feature, and the recorded lesson from three tranches is that people
    /// then lower the threshold instead of translating.
    /// </para>
    /// </remarks>
    private const double CoverageFloorPercent = 90.0;

    /// <summary>
    /// THE LOCALIZED-FILE REGISTRY. Every component REQ-UI-050 has taken to full coverage; every
    /// user-visible string in each of these resolves through <c>AppStrings</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This list is what makes the owner's "new UI is localized when written" policy (2026-07-31)
    /// enforceable rather than aspirational. A file on it must measure ZERO hardcoded sites, so an
    /// English literal added to one of these screens fails
    /// <see cref="LocalizedFilesNeverRegainAHardcodedString"/> on the same change instead of being
    /// found in a Hindi screenshot two phases later. Without it, translation cannot outrun the
    /// denominator: four clusters ship UI concurrently, and a converted page left unguarded
    /// re-accumulates English faster than a tranche can take it away.
    /// </para>
    /// <para>
    /// The list is APPEND-ONLY in practice, and <see cref="EveryCleanFileIsOnTheLocalizedFileRegistry"/>
    /// is what keeps it honest — deleting a row to make a red build go green immediately fails that
    /// test instead.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <c>internal</c> rather than private since REQ-UI-051, so <c>CodeBlockCoverageTests</c> gates
    /// the same files against the code-block scan. One registry: a copy would drift the first time
    /// a cluster appended to only one of them.
    /// </remarks>
    internal static readonly string[] LocalizedFiles =
    [
        // REQ-UI-050, first tranche (2026-07-29): the shell and the shared panels.
        "Layout/MainLayout.razor",
        "Shared/UpgradePrompt.razor",
        "Shared/LanguagePicker.razor",
        "Shared/ThemeToggle.razor",

        // REQ-UI-050, second tranche (2026-07-31): the six largest pages, ~781 sites.
        "Pages/QdrantAdmin.razor",
        "Pages/ConnectorEdit.razor",
        "Pages/WorkspaceAgents.razor",
        "Pages/Support.razor",
        "Pages/Automations.razor",
        "Pages/ConnectorsHub.razor",

        // REQ-UI-050, third tranche (2026-08-01): the next ten pages, 670 sites.
        "Pages/WorkspaceChat.razor",
        "Pages/Billing.razor",
        "Pages/DocumentLibrary.razor",
        "Pages/RagConfig.razor",
        "Pages/BackupRestore.razor",
        "Pages/Ingestion.razor",
        "Pages/LlmSettings.razor",
        "Pages/AdminEvents.razor",
        "Pages/AddFromWeb.razor",
        "Pages/TextIngestion.razor",

        // REQ-UI-040 (BRD-92), 2026-08-01: the flow builder, localized as it was written per the
        // owner's 2026-07-31 policy — it was never on this list in English.
        "Pages/WorkspaceFlows.razor",

        // REQ-UI-052 (BRD-91), 2026-08-01: the auth screens. They are the first thing an outside
        // user sees and they had never entered the programme at all — no tranche picked them up,
        // because they are small and the tranches were sorted by site count. The other half of that
        // requirement, the native menu bar, is built in C# and cannot be listed here; it is guarded
        // instead by MenuBarLocalizationTests, which reads MainPage.xaml.cs directly.
        "Layout/AuthLayout.razor",
        "Pages/Auth/Login.razor",
        "Pages/Auth/Register.razor",
        "Pages/Auth/ForgotPassword.razor",
        "Pages/Auth/ResetPassword.razor",

        // REQ-UI-050, fourth tranche (2026-08-01): everything that was left. Sixteen components,
        // 430 sites — the first-run wizard, the two remaining chat surfaces, and the last of the
        // shared panels. With these the component tree measures ZERO hardcoded markup sites.
        "Pages/Setup.razor",
        "Pages/Auth/Profile.razor",
        "Pages/WorkspaceSettings.razor",
        "Pages/AppUpdates.razor",
        "Pages/LlmPlayground.razor",
        "Pages/DataStorage.razor",
        "Pages/AdminSettings.razor",
        "Pages/Chat.razor",
        "Pages/TokenUsage.razor",
        "Pages/Pricing.razor",
        "Pages/Home.razor",
        "Shared/LicenseStatusCard.razor",
        "Shared/BrandingPanel.razor",
        "Shared/AppearancePanel.razor",
        "Shared/AgentTracePanel.razor",
        "Shared/DictationButton.razor",
    ];

    /// <summary>
    /// A text-bearing NAME is scored wherever it is written, and a name that merely ends in the
    /// same letters is not.
    /// </summary>
    /// <param name="source">A razor fragment.</param>
    /// <param name="expected">How many hardcoded attribute sites it should report.</param>
    /// <remarks>
    /// <para>
    /// The fixtures for the 2026-08-01 widening — see the remarks on
    /// <c>RazorStringCoverage.TextAttributes</c>. Every assertion the counter makes about a real
    /// file can only prove the ABSENCE of a finding now that the tree measures zero, so this is
    /// what says out loud what the rule IS, in both directions.
    /// </para>
    /// <para>
    /// The <c>Context</c> row is not hypothetical. The first cut of the suffix rule had no
    /// word-boundary check, <c>Context</c> matched "text", and every <c>DataTable</c> column in the
    /// app was reported as untranslated English — 64 phantom sites across eleven files that were
    /// already fully localized. It was caught by running the widened counter before trusting it,
    /// and this row is what stops it coming back.
    /// </para>
    /// </remarks>
    [Theory]
    // The class the widening exists for: a named field carrying an English sentence.
    [InlineData("@code {\n    void Fail() { loadError = \"The licence server could not be reached.\"; }\n}", 1)]
    [InlineData("@code {\n    void Step() { progressMessage = \"Chunking and embedding text...\"; }\n}", 1)]
    [InlineData("@code {\n    var options = new PickOptions { PickerTitle = \"Choose documents\" };\n}", 1)]
    // Localized: nothing to report.
    [InlineData("@code {\n    void Fail() { loadError = Localizer[\"BillingLoadFailed\"]; }\n}", 0)]
    // Markup, which is where this always worked.
    [InlineData("<Input Placeholder=\"Workspace name\" />", 1)]
    [InlineData("<Input Placeholder=\"@Localizer[\"WsSettingsNamePlaceholder\"]\" />", 0)]
    // NOT text: the suffix has to start a word. `Context` ends in "text" and is Blazor's
    // RenderFragment variable; `HeaderClass` and `TextChanged` are styling and an event.
    [InlineData("<CellTemplate Context=\"row\"><span>x</span></CellTemplate>", 0)]
    [InlineData("<DataTableColumn HeaderClass=\"td-th-label-hidden\" />", 0)]
    [InlineData("<DictationButton TextChanged=\"OnDictatedAsync\" />", 0)]
    // A brand or protocol noun on the untranslatable list is not a site wherever it is written.
    [InlineData("@code {\n    var host = new Row { hostText = \"GitHub\" };\n}", 0)]
    public void TextBearingNamesAreScoredWhereverTheyAreWritten(string source, int expected)
    {
        var row = RazorStringCoverage.MeasureSource(source, "Fixture.razor");

        Assert.Equal(expected, row.HardcodedAttributes);
    }

    /// <summary>Coverage never goes backwards, and the current figure is printed on every run.</summary>
    [Fact]
    public void LocalizedSiteCountNeverFalls()
    {
        var rows = RazorStringCoverage.Scan();
        var localized = rows.Sum(row => row.Localized);
        var total = rows.Sum(row => row.Total);
        var percent = 100d * localized / total;

        output.WriteLine($"AppStrings coverage: {localized}/{total} = {percent:F1}%");
        foreach (var row in rows.OrderByDescending(row => row.Hardcoded).Take(10))
        {
            output.WriteLine(
                $"  {row.Hardcoded,5} hardcoded ({row.HardcodedRuns} runs, " +
                $"{row.HardcodedAttributes} attrs, {row.HardcodedToasts} toasts)  {row.RelativePath}");
        }

        Assert.True(
            localized >= LocalizedSiteFloor,
            $"Localized string sites fell from {LocalizedSiteFloor} to {localized}. Coverage is a " +
            "ratchet: strings may be added to it, never taken out of it.");
    }

    /// <summary>Coverage as a share of every user-visible site stays above the agreed floor.</summary>
    [Fact]
    public void CoveragePercentageStaysAboveTheFloor()
    {
        var rows = RazorStringCoverage.Scan();
        var localized = rows.Sum(row => row.Localized);
        var total = rows.Sum(row => row.Total);
        var percent = 100d * localized / total;

        Assert.True(
            percent >= CoverageFloorPercent,
            $"Coverage is {percent:F1}% ({localized}/{total}), below the agreed {CoverageFloorPercent:F1}%. " +
            "Either localization was removed, or enough new English UI has landed that the next " +
            "tranche of REQ-UI-050 is due — the top offenders are printed by " +
            $"{nameof(LocalizedSiteCountNeverFalls)}.");
    }

    /// <summary>
    /// A file that has been localized stays localized: new UI in it is localized when written.
    /// </summary>
    /// <remarks>
    /// This is the enforcement half of the owner's 2026-07-31 policy — see the remarks on
    /// <see cref="LocalizedFiles"/>. It fails on the change that adds the English literal, naming
    /// the file and how many literals it found, so the fix is to translate the new string rather
    /// than to schedule a translation pass.
    /// </remarks>
    [Fact]
    public void LocalizedFilesNeverRegainAHardcodedString()
    {
        var rows = RazorStringCoverage.Scan()
            .ToDictionary(row => row.RelativePath, StringComparer.Ordinal);

        // Every offender is reported, not just the first: a builder fixing one file at a time from
        // six separate red runs is how a policy gets a reputation for being obstructive.
        var offenders = new List<string>();

        foreach (var path in LocalizedFiles)
        {
            Assert.True(rows.ContainsKey(path), $"{path} was not found by the scan.");

            var row = rows[path];
            if (row.Hardcoded > 0)
            {
                offenders.Add(
                    $"{path}: {row.Hardcoded} literal(s) — {row.HardcodedRuns} text runs, " +
                    $"{row.HardcodedAttributes} attributes, {row.HardcodedToasts} toasts");
            }

            Assert.True(row.Localized > 0, $"{path} resolves no strings at all — is it still wired up?");
        }

        Assert.True(
            offenders.Count == 0,
            "These files are on the localized-file registry but carry user-visible English " +
            "literals. New UI is localized when it is written, not in a later pass — route them " +
            "through IStringLocalizer<AppStrings> and add the keys to AppStrings.resx AND " +
            "AppStrings.hi.resx:" + Environment.NewLine + "  " +
            string.Join(Environment.NewLine + "  ", offenders));
    }

    /// <summary>
    /// A component that HAS reached zero hardcoded sites is on the registry, so the registry cannot
    /// be quietly shrunk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, <see cref="LocalizedFilesNeverRegainAHardcodedString"/> has an obvious escape:
    /// a builder who adds an English literal to a converted page can delete that page's row instead
    /// of translating the string, and the suite goes green having lost the guarantee. Here, removing
    /// a row fails immediately — the file is still clean, so it still belongs on the list.
    /// </para>
    /// <para>
    /// "Clean" means the file both uses the localizer and has no literals left. A component with no
    /// user-visible text at all (a pure container) resolves nothing and is not a localization
    /// claim, so it is not conscripted onto the registry.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCleanFileIsOnTheLocalizedFileRegistry()
    {
        var registered = LocalizedFiles.ToHashSet(StringComparer.Ordinal);

        var missing = RazorStringCoverage.Scan()
            .Where(row => row.Localized > 0 && row.Hardcoded == 0)
            .Select(row => row.RelativePath)
            .Where(path => !registered.Contains(path))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{missing.Length} component(s) are fully localized but absent from the " +
            $"{nameof(LocalizedFiles)} registry, so nothing stops English being added back to " +
            "them: " + string.Join(", ", missing) + ". Add them to the registry.");
    }

    /// <summary>
    /// The untranslatable-token list stays small, and every entry on it is still in use.
    /// </summary>
    /// <remarks>
    /// <see cref="RazorStringCoverage.Untranslatable"/> is the counter's one subjective input — a
    /// value on it is not counted as untranslated English — so it is also the obvious way to make
    /// <see cref="LocalizedFilesNeverRegainAHardcodedString"/> go green without translating
    /// anything. Two guards, both cheap: it cannot grow into a dumping ground, and it cannot be
    /// stuffed in advance, because an entry that no component actually renders fails here and has
    /// to be deleted. Neither proves an entry BELONGS on the list; that is a review question, and
    /// keeping the list short is what keeps the review possible.
    /// </remarks>
    [Fact]
    public void TheUntranslatableListStaysSmallAndInUse()
    {
        Assert.True(
            RazorStringCoverage.Untranslatable.Count <= 40,
            $"The untranslatable-token list has grown to {RazorStringCoverage.Untranslatable.Count} " +
            "entries. It exists for brand and protocol nouns, not as a way to excuse English UI.");

        var tree = string.Concat(
            Directory.GetFiles(RazorStringCoverage.FindComponentsRoot(), "*.razor", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        var unused = RazorStringCoverage.Untranslatable
            .Where(token => !tree.Contains(token, StringComparison.Ordinal))
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unused.Length == 0,
            "These untranslatable tokens are no longer rendered by any component, so they are " +
            "exempting nothing and must be removed: " + string.Join(", ", unused));
    }

    /// <summary>The scan really reads the app's components rather than silently finding nothing.</summary>
    /// <remarks>
    /// The counter's whole value is that it cannot be fooled, so the thing most worth testing is the
    /// path walk: a scan that returns an empty list would make every assertion above vacuous.
    /// </remarks>
    [Fact]
    public void ScanFindsTheRealComponentTree()
    {
        var rows = RazorStringCoverage.Scan();

        Assert.True(rows.Count >= 40, $"Only {rows.Count} components were scanned.");
        Assert.Contains(rows, row => row.RelativePath == "Layout/MainLayout.razor");
        Assert.Contains(rows, row => row.RelativePath == "Pages/AdminSettings.razor");
        Assert.True(rows.Sum(row => row.Total) > 1000, "The measured population is implausibly small.");
    }
}
