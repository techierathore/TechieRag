using Xunit;
using Xunit.Abstractions;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// REQ-UI-051 (BRD-91): the guard on user-visible English built inside a <c>@code</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StringCoverageTests"/> ratchets MARKUP, and a file on its registry must measure zero
/// hardcoded markup sites. REQ-UI-050's third tranche satisfied that and the product still rendered
/// English on a Hindi install, because the strings a page builds in C# — a composer placeholder, a
/// status pill's <c>switch</c>, a whole provider form written as a <c>RenderFragment</c> — are
/// outside the measured population by design, so nothing failed. The gap was found by looking at a
/// Hindi window, which is not a thing that scales, and an unmeasured gap always grows back.
/// </para>
/// <para>
/// These tests close it for the files that CLAIM to be localized. They are a zero-tolerance gate
/// rather than a ratchet: the registry's whole promise is "this screen is done", and a screen that
/// is done has no English left to count down from.
/// </para>
/// <para>
/// <b>Read <see cref="CodeBlockStringCoverage"/>'s remarks for what this cannot see.</b> The short
/// version: it finds English SENTENCES, not single words; it finds nothing outside the component
/// tree; and it cannot tell a good translation from a bad one.
/// </para>
/// </remarks>
public sealed class CodeBlockCoverageTests
{
    private readonly ITestOutputHelper output;

    /// <summary>Captures the xunit output sink so every run prints the current figure.</summary>
    /// <param name="output">The xunit output sink.</param>
    public CodeBlockCoverageTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// A file on the localized-file registry builds no English in its <c>@code</c> block.
    /// </summary>
    /// <remarks>
    /// This is the test REQ-UI-051 exists to add. Had it been present on 2026-08-01,
    /// <c>WorkspaceChat.ComposerPlaceholder()</c> — <c>$"Message {workspace?.Name}…"</c>, the single
    /// most-read string in the product — would have failed the change that shipped it, instead of
    /// being read off a screenshot a phase later.
    /// </remarks>
    [Fact]
    public void LocalizedFilesBuildNoEnglishInTheirCodeBlocks()
    {
        var rows = CodeBlockStringCoverage.Scan()
            .ToDictionary(row => row.RelativePath, StringComparer.Ordinal);

        var root = RazorStringCoverage.FindComponentsRoot();
        var offenders = new List<string>();

        foreach (var path in StringCoverageTests.LocalizedFiles)
        {
            // A component with no @code block at all has nothing to measure and is not a failure.
            if (!rows.TryGetValue(path, out var row) || row.Hardcoded == 0)
            {
                continue;
            }

            var literals = CodeBlockStringCoverage.ProseLiteralsIn(
                File.ReadAllText(Path.Combine(root, path)));

            offenders.Add(
                CodeBlockStringCoverage.Describe(row) +
                (literals.Count == 0
                    ? string.Empty
                    : Environment.NewLine + "      " +
                      string.Join(Environment.NewLine + "      ", literals.Select(l => $"\"{l}\""))));
        }

        Assert.True(
            offenders.Count == 0,
            "These files are on the localized-file registry, so they measure ZERO hardcoded markup " +
            "— and they still build English the user reads. Route it through " +
            "IStringLocalizer<AppStrings> and add the key to AppStrings.resx AND AppStrings.hi.resx. " +
            "A value that is persisted or compared stays invariant: give it a name ending in " +
            "'Sentinel' and localize the DISPLAY of it." + Environment.NewLine + "  " +
            string.Join(Environment.NewLine + "  ", offenders));
    }

    /// <summary>The scan really reads the app's components rather than silently finding nothing.</summary>
    /// <remarks>
    /// A path walk that quietly resolved to an empty directory would make
    /// <see cref="LocalizedFilesBuildNoEnglishInTheirCodeBlocks"/> pass forever, which is the same
    /// shape of hole REQ-UI-051 exists to close. The current figure is printed on every run so the
    /// tree's remaining <c>@code</c> English is visible rather than inferred.
    /// </remarks>
    [Fact]
    public void TheScanReadsTheRealComponentTree()
    {
        var rows = CodeBlockStringCoverage.Scan();

        Assert.True(rows.Count >= 20, $"Only {rows.Count} components with a @code block were found.");
        Assert.Contains(rows, row => row.RelativePath == "Pages/WorkspaceChat.razor");
        Assert.Contains(rows, row => row.RelativePath == "Pages/LlmSettings.razor");

        var found = rows.Sum(row => row.Hardcoded);
        output.WriteLine($"@code sites across the tree: {found} in {rows.Count} components");
        foreach (var row in rows.Where(row => row.Hardcoded > 0).OrderByDescending(row => row.Hardcoded).Take(10))
        {
            output.WriteLine("  " + CodeBlockStringCoverage.Describe(row));
        }
    }

    /// <summary>
    /// Putting the original defect back into the real component makes the scan report it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The anti-vacuity test, and the more important half of the pair: a gate that only ever asserts
    /// the ABSENCE of a finding goes green the day somebody breaks the detector, and the defect
    /// walks straight back in — which is exactly how REQ-UI-051 happened, so it is not a
    /// hypothetical failure mode here.
    /// </para>
    /// <para>
    /// It mutates the REAL <c>WorkspaceChat.razor</c> in memory rather than a hand-written fixture,
    /// so it exercises the whole path — finding the tree, brace-matching a two-thousand-line file's
    /// code block, separating the C# from the template regions — on the file the requirement was
    /// actually raised against. Nothing is written to disk.
    /// </para>
    /// </remarks>
    [Fact]
    public void DetectsTheOriginalDefectPutBackIntoTheRealComponent()
    {
        const string Path = "Pages/WorkspaceChat.razor";
        const string Localized = "Localizer[\"ChatComposerPlaceholder\", workspace?.Name ?? string.Empty]";
        const string Original = "$\"Message {workspace?.Name}…\\n\\nShift + Return for a new line, Return to send.\"";

        var source = File.ReadAllText(
            System.IO.Path.Combine(RazorStringCoverage.FindComponentsRoot(), Path));

        Assert.Contains(Localized, source, StringComparison.Ordinal);

        var clean = CodeBlockStringCoverage.Measure(source, Path);
        Assert.NotNull(clean);
        Assert.Equal(0, clean!.Hardcoded);

        var reverted = CodeBlockStringCoverage.Measure(
            source.Replace(Localized, Original, StringComparison.Ordinal), Path);

        Assert.NotNull(reverted);
        Assert.Equal(1, reverted!.ProseLiterals);
        Assert.Contains(
            CodeBlockStringCoverage.ProseLiteralsIn(
                source.Replace(Localized, Original, StringComparison.Ordinal)),
            literal => literal.Contains("Shift + Return", StringComparison.Ordinal));
    }

    /// <summary>
    /// The detector recognises the exact shape REQ-UI-051 was raised for, and the shapes it must
    /// leave alone.
    /// </summary>
    /// <param name="fragment">A <c>@code</c> block body.</param>
    /// <param name="expected">How many prose literals it should report.</param>
    /// <remarks>
    /// The registry gate above can only ever prove the ABSENCE of a finding, so on the day somebody
    /// breaks <c>IsProse</c> it would go green rather than red. These are the fixtures that say what
    /// the rule actually is, in both directions: the composer placeholder is the string the
    /// requirement was written about, and the wire codes, format strings, ids and logger templates
    /// below are the four classes that made a naive version of this test unusable.
    /// </remarks>
    [Theory]
    // The defect itself.
    [InlineData("private string Placeholder() => $\"Message {name}…\\n\\nReturn to send.\";", 1)]
    [InlineData("private string Label() => \"Whole workspace\";", 1)]
    [InlineData("_ => \"Nothing was ingested\",", 1)]
    // Localized: nothing to report.
    [InlineData("private string Label() => Localizer[\"ChatScopeLabelWholeWorkspace\"];", 0)]
    // One word is out of scope by design — see the class remarks.
    [InlineData("_ => \"Failed\",", 0)]
    // Element ids, wire codes and CSS-ish tokens: no space, so not prose.
    [InlineData("private const string TargetId = \"support-new-issue-paste\";", 0)]
    // Connection strings and URLs.
    [InlineData("_ => \"Data Source=techierag.db\",", 0)]
    [InlineData("_ => \"https://docs.example.com/\",", 0)]
    // A .NET format string is a contract with the runtime.
    [InlineData("var stamp = when.ToString(\"dd MMM yyyy, HH:mm:ss\");", 0)]
    // A structured-logging template is read in a log viewer.
    [InlineData("Logger.LogError(ex, \"Failed to load workspace {Slug}\", Slug);", 0)]
    // Interpolation holes are identifiers, not words.
    [InlineData("return $\"{sign}{magnitude} {code}\";", 0)]
    // A persisted value declares itself invariant by its name.
    [InlineData("private const string UntitledThreadSentinel = \"New conversation\";", 0)]
    // A Tailwind utility-class list is eight words and no English.
    [InlineData("private const string InputClass = \"flex h-10 w-full rounded-md border px-3 py-2\";", 0)]
    // ...but one hyphenated word in a sentence is still a sentence.
    [InlineData("_ => \"Read-only queries against a configured database\",", 1)]
    public void RecognisesProseAndLeavesMachineTextAlone(string fragment, int expected)
    {
        var source = "@code {\n    " + fragment + "\n}\n";

        var row = CodeBlockStringCoverage.Measure(source, "Fixture.razor");

        Assert.NotNull(row);
        Assert.Equal(expected, row!.ProseLiterals);
    }

    /// <summary>
    /// Markup written inside a <c>RenderFragment</c> is held to the same bar as markup written
    /// outside one.
    /// </summary>
    /// <remarks>
    /// The second half of the gap, and the one that hid the most text: <c>LlmSettings</c>'s entire
    /// provider form — every field label, every placeholder, every help line and the "direct chat is
    /// disabled" warning — lived inside <c>RenderLlmFields</c>, so the markup counter never saw a
    /// character of it and the page measured zero hardcoded sites while rendering an English form.
    /// </remarks>
    [Fact]
    public void FindsEnglishMarkupInsideARenderFragment()
    {
        var source =
            "@code {\n" +
            "    private RenderFragment Render() => __builder =>\n" +
            "    {\n" +
            "        <Field>\n" +
            "            <FieldLabel>Source</FieldLabel>\n" +
            "            <Input Placeholder=\"Enter API key\" />\n" +
            "        </Field>\n" +
            "    };\n" +
            "}\n";

        var row = CodeBlockStringCoverage.Measure(source, "Fixture.razor");

        Assert.NotNull(row);
        Assert.True(
            row!.FragmentAttributes > 0,
            "The literal Placeholder inside the RenderFragment was not counted.");
        Assert.True(
            row.FragmentRuns > 0,
            "The literal text run inside the RenderFragment was not counted.");
    }

    /// <summary>
    /// The code-block extractor is not fooled by a brace inside a string.
    /// </summary>
    /// <remarks>
    /// Worth its own test because the failure is SILENT and total: a block that ends early takes
    /// everything after it out of the scan, and the gate above then passes on a file it never read.
    /// <c>$"Found {n} file(s)"</c> and a literal <c>"}"</c> are both ordinary in these pages.
    /// </remarks>
    [Fact]
    public void ReadsTheWholeCodeBlockPastBracesInsideStrings()
    {
        var source =
            "<div>@Thing()</div>\n" +
            "@code {\n" +
            "    private string Closing() => \"}\";\n" +
            "    private string Opening() => \"{\";\n" +
            "    private string Late() => \"Nothing was ingested\";\n" +
            "}\n";

        var row = CodeBlockStringCoverage.Measure(source, "Fixture.razor");

        Assert.NotNull(row);
        Assert.Equal(1, row!.ProseLiterals);
    }

    /// <summary>
    /// The machine-text exemption list stays small, and every entry on it is still in use.
    /// </summary>
    /// <remarks>
    /// The same two guards <c>StringCoverageTests.TheUntranslatableListStaysSmallAndInUse</c> puts
    /// on the markup counter's exemption list, and for the same reason: this list is the obvious way
    /// to make <see cref="LocalizedFilesBuildNoEnglishInTheirCodeBlocks"/> go green without
    /// translating anything. Neither guard proves an entry BELONGS on the list — that is a review
    /// question, and keeping the list short is what keeps the review possible.
    /// </remarks>
    [Fact]
    public void TheMachineTextListStaysSmallAndInUse()
    {
        Assert.True(
            CodeBlockStringCoverage.MachineText.Count <= 20,
            $"The machine-text list has grown to {CodeBlockStringCoverage.MachineText.Count} " +
            "entries. It exists for brand names and text addressed to the model, not as a way to " +
            "excuse English UI.");

        var tree = string.Concat(
            Directory.GetFiles(RazorStringCoverage.FindComponentsRoot(), "*.razor", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        var unused = CodeBlockStringCoverage.MachineText
            .Where(token => !tree.Contains(token, StringComparison.Ordinal))
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unused.Length == 0,
            "These machine-text entries are no longer present in any component, so they are " +
            "exempting nothing and must be removed: " + string.Join(", ", unused));
    }
}
