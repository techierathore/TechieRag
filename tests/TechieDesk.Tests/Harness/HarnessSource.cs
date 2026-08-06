using System.Text;
using System.Text.RegularExpressions;

namespace TechieDesk.Tests.Harness;

/// <summary>
/// One <c>&lt;SidebarMenuButton&gt;</c> exactly as <c>MainLayout.razor</c> declares it.
/// </summary>
/// <param name="RouteTemplate">
/// The <c>Href</c>, normalised so the per-install workspace slug is <c>{slug}</c> — e.g.
/// <c>/workspace/{slug}/documents</c>, <c>/settings/backup</c>.
/// </param>
/// <param name="AutomationId">The literal <c>id="…"</c> on the button (REQ-UI-053).</param>
/// <param name="ResourceKey">The key of the <c>&lt;span&gt;@Localizer["…"]&lt;/span&gt;</c> label.</param>
/// <param name="Ordinal">Its position in the file, so a failure can name the button.</param>
public sealed record SidebarButton(
    string RouteTemplate,
    string AutomationId,
    string ResourceKey,
    int Ordinal);

/// <summary>One string constant found in the Appium harness source.</summary>
/// <param name="FileName">The harness file it was written in, e.g. <c>run_sweep.py</c>.</param>
/// <param name="Line">Its 1-based line number, so a failure points at it.</param>
/// <param name="Value">The literal's text, before any escape processing.</param>
/// <param name="IsFormatString">Whether it carried an <c>f</c> prefix (a message template).</param>
/// <param name="IsTripleQuoted">Whether it is a docstring / prose block.</param>
public sealed record PythonLiteral(
    string FileName,
    int Line,
    string Value,
    bool IsFormatString,
    bool IsTripleQuoted);

/// <summary>
/// Reads the two halves of the sweep harness's contract with the product — the sidebar as
/// <c>MainLayout.razor</c> declares it, and the tables <c>tests/appium/</c> navigates it by
/// (REQ-NFR-014).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this reads source rather than running anything.</b> The failure this exists to stop is a
/// harness that is SILENTLY INCOMPLETE: <c>/settings/backup</c> shipped, was never added to
/// <c>run_sweep.SIDEBAR</c>, and therefore was never graded by any verify run — while the sweep
/// reported all-clean over the list it did know about. Nothing at runtime can see that, because the
/// harness never asks about the screen it has forgotten. The only place the omission is visible is
/// the DIFFERENCE between two files on disk, which is what this class computes.
/// </para>
/// <para>
/// <b>Why it is a C# test and not a python check inside the harness.</b> Three reasons. It must run
/// on the change that breaks it: <c>dotnet test</c> is what the build phase and <c>verify-phase</c>
/// §5 already run, whereas a python check only runs when somebody runs the sweep — and the sweep is
/// precisely the thing that is incomplete, so a guard living inside it is guarded by its own
/// subject. It needs no Appium session, no macOS and no running head, so it cannot be skipped for
/// environmental reasons. And the resource table it has to validate keys against is already
/// available here as <c>IStringLocalizer&lt;AppStrings&gt;</c>, which is the product's own view of
/// its strings rather than a second parser of the same <c>.resx</c>.
/// </para>
/// <para>
/// Every parse below is anchored on <c>TechieRag.slnx</c> and THROWS when a file is missing, and
/// every caller asserts a plausible count before asserting anything else — the
/// <see cref="TechieDesk.Tests.Localization.RazorStringCoverage.FindComponentsRoot"/> discipline.
/// A source scan that quietly matches nothing is the same defect as the incomplete sweep table.
/// </para>
/// </remarks>
public static class HarnessSource
{
    /// <summary>A whole <c>&lt;SidebarMenuButton …&gt;…&lt;/SidebarMenuButton&gt;</c> element.</summary>
    private static readonly Regex SidebarButtonBlock = new(
        @"<SidebarMenuButton\b(.*?)</SidebarMenuButton>", RegexOptions.Singleline);

    /// <summary><c>Href="@($"/workspace/{workspaceSlug}/agents")"</c> — an interpolated route.</summary>
    private static readonly Regex InterpolatedHref = new(@"Href=""@\(\$""([^""]*)""\)""");

    /// <summary><c>Href="/settings/backup"</c> — a plain route.</summary>
    private static readonly Regex PlainHref = new(@"Href=""(/[^""@]*)""");

    /// <summary>The button's REQ-UI-053 identifier: <c>id="nav-settings-backup"</c>.</summary>
    private static readonly Regex ButtonIdentifier = new(@"\bid=""([^""]+)""");

    /// <summary>The visible label: <c>&lt;span&gt;@Localizer["NavBackupRestore"]&lt;/span&gt;</c>.</summary>
    private static readonly Regex ButtonLabelKey = new(
        @"<span>\s*@Localizer\[\s*""([^""]+)""\s*\]\s*</span>", RegexOptions.Singleline);

    /// <summary>A <c>(slug, route, key)</c> row of <c>run_sweep.SIDEBAR</c>.</summary>
    private static readonly Regex SweepRow = new(
        @"\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\)");

    /// <summary>A <c>"key": "value"</c> pair of a python dict literal.</summary>
    private static readonly Regex DictPair = new(@"""([^""]+)""\s*:\s*""([^""]*)""");

    /// <summary>A module-level constant: <c>SIDEBAR = […]</c> at column zero.</summary>
    /// <remarks>
    /// The name pattern allows underscores because python constants use them —
    /// <c>NAV_IDS</c>, <c>STANDARD_TITLES</c>, <c>GO_MENU_KEY</c>. Spelling it
    /// <c>[A-Z][A-Za-z0-9]*</c> to honour the C# no-underscore rule would silently skip most of
    /// the harness's tables, which is the very failure this file guards against.
    /// </remarks>
    private static readonly Regex ModuleConstant = new(
        @"^([A-Z][A-Za-z0-9_]*)\s*=(?!=)", RegexOptions.Multiline);

    /// <summary>Reads a single-string module constant, e.g. <c>GO_MENU_KEY = "MenuGo"</c>.</summary>
    /// <param name="fileName">The harness file, e.g. <c>nav.py</c>.</param>
    /// <param name="name">The constant's name.</param>
    /// <returns>Its value.</returns>
    /// <exception cref="InvalidOperationException">The constant is absent or no longer a literal.</exception>
    public static string ScalarConstant(string fileName, string name)
    {
        var match = Regex.Match(
            File.ReadAllText(HarnessPath(fileName)),
            $@"^{Regex.Escape(name)}\s*=\s*""([^""]*)""",
            RegexOptions.Multiline);

        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException(
                $"{name} is not a plain string constant in {fileName} any more, so the guard that " +
                "checks it would assert nothing.");
    }

    /// <summary>Gets the absolute path of the repository root.</summary>
    /// <returns>The directory holding <c>TechieRag.slnx</c>.</returns>
    /// <exception cref="InvalidOperationException">The repository root was not found.</exception>
    /// <remarks>
    /// Anchored on the solution file rather than on a hop count from the test output directory, and
    /// it throws: a path walk that silently finds nothing turns every assertion below into a test
    /// that passes forever, which is the exact defect class this file exists to close.
    /// </remarks>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechieRag.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException(
            "Could not find TechieRag.slnx above " + AppContext.BaseDirectory +
            ", so neither the sidebar markup nor the Appium harness could be located.");
    }

    /// <summary>Gets the absolute path of <c>MainLayout.razor</c>.</summary>
    /// <returns>The layout that declares every sidebar destination.</returns>
    /// <exception cref="InvalidOperationException">The layout is not where it is expected.</exception>
    public static string SidebarMarkupPath() => Existing(Path.Combine(
        RepositoryRoot(), "apps", "TechieDesk", "Components", "Layout", "MainLayout.razor"));

    /// <summary>Gets the absolute path of a file in <c>tests/appium/</c>.</summary>
    /// <param name="fileName">The harness file's name, e.g. <c>run_sweep.py</c>.</param>
    /// <returns>Its absolute path.</returns>
    /// <exception cref="InvalidOperationException">The harness file is missing.</exception>
    public static string HarnessPath(string fileName) =>
        Existing(Path.Combine(RepositoryRoot(), "tests", "appium", fileName));

    /// <summary>Gets every python file of the Appium harness, in name order.</summary>
    /// <returns>The absolute paths.</returns>
    /// <exception cref="InvalidOperationException">The harness directory is missing or implausible.</exception>
    public static IReadOnlyList<string> HarnessFiles()
    {
        var directory = Path.Combine(RepositoryRoot(), "tests", "appium");
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"'{directory}' does not exist.");
        }

        var files = Directory.GetFiles(directory, "*.py", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return files.Length >= 5
            ? files
            : throw new InvalidOperationException(
                $"Only {files.Length} python file(s) under '{directory}'. The harness has more, so " +
                "this scan is reading the wrong tree and every assertion over it would be vacuous.");
    }

    /// <summary>Reads every sidebar destination the app actually declares.</summary>
    /// <returns>One entry per <c>&lt;SidebarMenuButton&gt;</c>, in file order.</returns>
    /// <exception cref="InvalidOperationException">A button is missing a route, an id or a label key.</exception>
    /// <remarks>
    /// A button that does not yield all three is an ERROR rather than a skipped row. Skipping it
    /// would let a malformed (or newly-shaped) entry drop out of the comparison unnoticed — which is
    /// the incompleteness this whole file is about, reproduced inside the guard itself.
    /// </remarks>
    public static IReadOnlyList<SidebarButton> DeclaredSidebarButtons()
    {
        var markup = File.ReadAllText(SidebarMarkupPath());
        var buttons = new List<SidebarButton>();
        var ordinal = 0;

        foreach (Match block in SidebarButtonBlock.Matches(markup))
        {
            var body = block.Groups[1].Value;
            ordinal++;

            var interpolated = InterpolatedHref.Match(body);
            var href = interpolated.Success ? interpolated.Groups[1].Value : PlainHref.Match(body).Groups[1].Value;
            var identifier = ButtonIdentifier.Match(body).Groups[1].Value;
            var labelKey = ButtonLabelKey.Match(body).Groups[1].Value;

            if (href.Length == 0 || identifier.Length == 0 || labelKey.Length == 0)
            {
                throw new InvalidOperationException(
                    $"SidebarMenuButton #{ordinal} in MainLayout.razor did not yield all three of " +
                    $"Href / id / <span>@Localizer[…]</span> (got href='{href}', id='{identifier}', " +
                    $"key='{labelKey}'). Either the button is malformed, or its shape changed and " +
                    "HarnessSource's regexes must change with it — do NOT let it be skipped.");
            }

            buttons.Add(new SidebarButton(NormaliseRoute(href), identifier, labelKey, ordinal));
        }

        return buttons;
    }

    /// <summary>Reads the <c>(slug, route, resource-key)</c> table the sweep navigates by.</summary>
    /// <returns>The rows of <c>run_sweep.SIDEBAR</c>, in file order.</returns>
    /// <exception cref="InvalidOperationException">The table could not be located.</exception>
    public static IReadOnlyList<(string Slug, string Route, string Key)> SweepSidebarTable()
    {
        var block = Block(File.ReadAllText(HarnessPath("run_sweep.py")), "SIDEBAR = [", ']');
        return SweepRow.Matches(block)
            .Select(row => (row.Groups[1].Value, NormaliseRoute(row.Groups[2].Value), row.Groups[3].Value))
            .ToArray();
    }

    /// <summary>Reads <c>nav.NAV_IDS</c> — resource key to the link's route-derived identifier.</summary>
    /// <returns>The mapping, in file order.</returns>
    /// <exception cref="InvalidOperationException">The table could not be located.</exception>
    public static IReadOnlyList<(string Key, string AutomationId)> NavIdTable() =>
        DictPair.Matches(Block(File.ReadAllText(HarnessPath("nav.py")), "NAV_IDS = {", '}'))
            .Select(pair => (pair.Groups[1].Value, pair.Groups[2].Value))
            .ToArray();

    /// <summary>Reads <c>run_sweep.CHROMELESS</c> — the arrival markers of the sidebar-less screens.</summary>
    /// <returns>The slug-to-marker mapping, in file order.</returns>
    /// <exception cref="InvalidOperationException">The table could not be located.</exception>
    public static IReadOnlyList<(string Slug, string Marker)> ChromelessMarkers() =>
        DictPair.Matches(Block(File.ReadAllText(HarnessPath("run_sweep.py")), "CHROMELESS = {", '}'))
            .Select(pair => (pair.Groups[1].Value, pair.Groups[2].Value))
            .ToArray();

    /// <summary>Reads <c>menu_check.STANDARD_TITLES</c> — the macOS-owned menu titles.</summary>
    /// <returns>The declared-title to stock-title mapping, in file order.</returns>
    /// <exception cref="InvalidOperationException">The table could not be located.</exception>
    public static IReadOnlyList<(string Declared, string Stock)> StandardMenuTitles() =>
        DictPair.Matches(Block(File.ReadAllText(HarnessPath("menu_check.py")), "STANDARD_TITLES = {", '}'))
            .Select(pair => (pair.Groups[1].Value, pair.Groups[2].Value))
            .ToArray();

    /// <summary>
    /// Lists the module-level constants of one harness file whose value contains a string literal.
    /// </summary>
    /// <param name="fileName">The harness file, e.g. <c>nav.py</c>.</param>
    /// <returns>The constant names, in ordinal order.</returns>
    /// <remarks>
    /// The inventory a new selector table shows up in. A constant whose value holds no quote at all
    /// (<c>SIDEBAR_XMAX = 340</c>) cannot carry a selector and is left out, so the caller's registry
    /// only has to classify things that could actually be matched against what the app renders.
    /// </remarks>
    public static IReadOnlyList<string> StringBearingConstants(string fileName)
    {
        var source = File.ReadAllText(HarnessPath(fileName));
        var names = new List<string>();

        foreach (Match match in ModuleConstant.Matches(source))
        {
            // The value runs to the next line that starts at column zero — which is where the next
            // top-level statement begins — so a multi-line table is read whole.
            var start = match.Index + match.Length;
            var end = NextTopLevelStatement(source, start);
            if (source[start..end].IndexOfAny(['"', '\'']) >= 0)
            {
                names.Add(match.Groups[1].Value);
            }
        }

        return names;
    }

    /// <summary>
    /// Reads every string constant written in a harness file.
    /// </summary>
    /// <param name="path">The python file's absolute path.</param>
    /// <returns>One record per literal, in file order.</returns>
    /// <remarks>
    /// <para>
    /// A hand-written scanner rather than a regex, because the alternative gets the harness wrong in
    /// a way that HIDES selectors. <c>strings.py</c> writes <c>.replace("#", "%23")</c>, so stripping
    /// <c>#</c> comments before extracting strings truncates the file mid-scan and everything after
    /// it silently stops being examined. Strings and comments have to be recognised in one
    /// left-to-right pass, which is what this does.
    /// </para>
    /// <para>
    /// Triple-quoted strings are reported as such: every one of them in this harness is a docstring,
    /// no selector is written as one, and they are full of the English prose that documents exactly
    /// the failures being guarded against. An <c>f</c> prefix is likewise reported — an f-string is a
    /// message template, never a selector, and its literal fragments ("." at the end of a sentence)
    /// are pure noise.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PythonLiteral> StringLiterals(string path)
    {
        var source = File.ReadAllText(path);
        var fileName = Path.GetFileName(path);
        var literals = new List<PythonLiteral>();
        var line = 1;
        var index = 0;

        while (index < source.Length)
        {
            var current = source[index];

            if (current == '\n')
            {
                line++;
                index++;
                continue;
            }

            if (current == '#')
            {
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (current is not ('"' or '\''))
            {
                index++;
                continue;
            }

            var prefix = PrefixAt(source, index);
            var startLine = line;
            var value = ReadLiteral(source, ref index, ref line, out var tripleQuoted);
            literals.Add(new PythonLiteral(
                fileName,
                startLine,
                value,
                prefix.Contains('f') || prefix.Contains('F'),
                tripleQuoted));
        }

        return literals;
    }

    /// <summary>
    /// Normalises a route so the razor <c>Href</c> and the sweep's route are the same string.
    /// </summary>
    /// <param name="route">A route from either side, e.g. <c>/workspace/default/agents</c>.</param>
    /// <returns>The route with the per-install workspace slug replaced by <c>{slug}</c>.</returns>
    /// <remarks>
    /// The workspace slug is the ONE segment that legitimately differs: the markup interpolates
    /// <c>{workspaceSlug}</c> and the sweep hardcodes the install's own <c>default</c>. Everything
    /// else must match character for character, so a route rename fails.
    /// </remarks>
    public static string NormaliseRoute(string route)
    {
        var segments = route.Trim('/').Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].StartsWith('{') || (i == 1 && segments[0] == "workspace"))
            {
                segments[i] = "{slug}";
            }
        }

        return "/" + string.Join('/', segments);
    }

    /// <summary>Gets the source text of a bracketed table, opener and closer included.</summary>
    private static string Block(string source, string opener, char closer)
    {
        var start = source.IndexOf(opener, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException(
                $"'{opener}' is not in the harness source. The table was renamed or restructured, so " +
                "this guard would compare against nothing — update HarnessSource with it.");
        }

        var end = source.IndexOf(closer, start + opener.Length);
        return end < 0 ? source[start..] : source[start..(end + 1)];
    }

    /// <summary>Gets the offset of the next statement starting at column zero, or the end of file.</summary>
    private static int NextTopLevelStatement(string source, int from)
    {
        for (var i = source.IndexOf('\n', from); i >= 0 && i + 1 < source.Length; i = source.IndexOf('\n', i + 1))
        {
            if (!char.IsWhiteSpace(source[i + 1]))
            {
                return i;
            }
        }

        return source.Length;
    }

    /// <summary>Gets the string-literal prefix immediately before <paramref name="quote"/>.</summary>
    /// <remarks>
    /// At most two characters, and only when what precedes them is not an identifier character —
    /// otherwise the <c>f</c> of <c>if</c> reads as an f-string prefix and a real selector written
    /// as <c>if"…"</c> would be dismissed as a message template.
    /// </remarks>
    private static string PrefixAt(string source, int quote)
    {
        var start = quote;
        while (start > 0 && quote - start < 2 && "rRbBuUfF".Contains(source[start - 1]))
        {
            start--;
        }

        if (start > 0 && (char.IsLetterOrDigit(source[start - 1]) || source[start - 1] == '_'))
        {
            return string.Empty;
        }

        return source[start..quote];
    }

    /// <summary>Consumes one string literal, advancing the cursor and the line counter past it.</summary>
    private static string ReadLiteral(string source, ref int index, ref int line, out bool tripleQuoted)
    {
        var quote = source[index];
        tripleQuoted = index + 2 < source.Length && source[index + 1] == quote && source[index + 2] == quote;
        var delimiter = tripleQuoted ? new string(quote, 3) : quote.ToString();
        index += delimiter.Length;

        var value = new StringBuilder();
        while (index < source.Length)
        {
            if (source[index] == '\\' && index + 1 < source.Length)
            {
                value.Append(source[index]).Append(source[index + 1]);
                if (source[index + 1] == '\n')
                {
                    line++;
                }

                index += 2;
                continue;
            }

            if (string.CompareOrdinal(source, index, delimiter, 0, delimiter.Length) == 0)
            {
                index += delimiter.Length;
                return value.ToString();
            }

            if (source[index] == '\n')
            {
                line++;
            }

            value.Append(source[index]);
            index++;
        }

        return value.ToString();
    }

    /// <summary>Returns the path, or throws naming it.</summary>
    private static string Existing(string path) => File.Exists(path)
        ? path
        : throw new InvalidOperationException(
            $"'{path}' does not exist, so this guard is reading the wrong tree.");
}
