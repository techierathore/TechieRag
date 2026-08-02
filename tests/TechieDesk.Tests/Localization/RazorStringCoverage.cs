using System.Text.RegularExpressions;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// One .razor component's localization coverage (REQ-UI-050 / BRD-91).
/// </summary>
/// <param name="RelativePath">The component's path below <c>Components/</c>, e.g. <c>Layout/MainLayout.razor</c>.</param>
/// <param name="Localized">User-visible sites that resolve through <c>IStringLocalizer</c>.</param>
/// <param name="HardcodedRuns">Literal prose written directly between markup tags.</param>
/// <param name="HardcodedAttributes">Literal values on attributes a user reads or hears.</param>
/// <param name="HardcodedToasts">Literal titles and messages passed to <c>ToastService</c>.</param>
public sealed record RazorFileCoverage(
    string RelativePath,
    int Localized,
    int HardcodedRuns,
    int HardcodedAttributes,
    int HardcodedToasts)
{
    /// <summary>Gets the number of user-visible sites that are still English literals.</summary>
    public int Hardcoded => HardcodedRuns + HardcodedAttributes + HardcodedToasts;

    /// <summary>Gets every user-visible string site in the file, localized or not.</summary>
    public int Total => Hardcoded + Localized;
}

/// <summary>
/// Counts the user-visible string sites in the TechieDesk razor components, and how many of them
/// route through <c>AppStrings</c> (REQ-UI-050 / BRD-91).
/// </summary>
/// <remarks>
/// <para>
/// This is the scripted counter that produced the 2026-07-29 baseline, kept in the repository and
/// run as a test instead of living in someone's shell history. The measurement it reproduces was
/// "45 of 1,928 sites = 2.3%"; this implementation reads the same tree as 45 of 2,002, the small
/// difference being how finely a multi-line text run is split. What matters is that ONE counter now
/// produces both the before and the after number, so the delta is real rather than a comparison of
/// two different definitions.
/// </para>
/// <para>
/// A "site" is a position in MARKUP (or in a toast call) where English text reaches the user:
/// a literal text run between tags, a literal value on a text-bearing attribute, or a literal
/// argument to <c>ToastService</c>. Labels that live as C# literals behind a single markup position
/// — <c>MainLayout</c>'s breadcrumb map is the example — are deliberately OUT of the population in
/// both the numerator and the denominator: they are one site however many branches feed it, and
/// counting the branches would let a refactor move the percentage without translating anything.
/// Localizing them is still required (an English crumb over a Hindi page is the same defect), it is
/// simply not what this number measures.
/// </para>
/// <para>
/// Every heuristic here is deliberately conservative in the direction of OVER-counting hardcoded
/// text: a counter that flatters the coverage figure is worse than no counter.
/// </para>
/// <para>
/// <b>2026-07-31 — three false-positive classes corrected.</b> Once the six largest pages were
/// converted, the phantoms were all that stood between them and zero, so they stopped being
/// harmless conservatism and started demanding that real text be "fixed" that was never English
/// prose. Each correction is a change to the DEFINITION of a site, applies to every file equally,
/// and can only ever remove HARDCODED sites — the numerator is untouched, so the ratchet's history
/// stays comparable. They are: literal machine text inside <c>&lt;code&gt;/&lt;kbd&gt;/&lt;samp&gt;/&lt;pre&gt;</c>
/// (see <c>LiteralElement</c>); C# that the tag-stripper mangles into prose — chained calls,
/// three-deep argument lists, <c>switch</c> labels, lambda arrows and generic-typed declarations in
/// a <c>@{ }</c> block (<c>MemberExpression</c>, <c>Statement</c>, <c>GenericDeclaration</c>); and
/// the short exact-match <see cref="Untranslatable"/> list of brand and protocol nouns. Together
/// they took the measured population from 2,067 to 2,036.
/// </para>
/// <para>
/// <b>THE "LOCALIZED WHEN WRITTEN" POLICY (owner decision, 2026-07-31).</b> New UI is localized as
/// it is written; it is not added in English for a later translation pass to find. Localization
/// cannot otherwise outrun the denominator — four clusters ship UI concurrently, so any file left
/// to "catch up later" is re-earning ground faster than a translation tranche can take it. The
/// policy is enforced, not aspirational: every file listed in
/// <c>StringCoverageTests.LocalizedFiles</c> must measure ZERO hardcoded sites, so adding an
/// English literal to one of them fails <c>LocalizedFilesNeverRegainAHardcodedString</c> on the
/// same change rather than being found in a screenshot two phases later. A file joins that list
/// the moment it reaches zero — <c>EveryCleanFileIsOnTheLocalizedFileRegistry</c> enforces the
/// joining, so the registry cannot be quietly shrunk to make a red build go green.
/// </para>
/// <para>
/// If you are adding UI to a listed file: inject <c>IStringLocalizer&lt;AppStrings&gt;</c>, write
/// <c>@Localizer["YourKey"]</c>, and add the key to BOTH <c>AppStrings.resx</c> and
/// <c>AppStrings.hi.resx</c>. A key missing from the Hindi file does not fail on its own — it
/// silently renders English inside a Hindi screen — which is what
/// <c>LocalizationTests.ResolvesEveryKeyTheRazorComponentsAskFor</c> is for.
/// </para>
/// </remarks>
public static class RazorStringCoverage
{
    private const RegexOptions Multi = RegexOptions.Multiline | RegexOptions.IgnoreCase;
    private const RegexOptions Single = RegexOptions.Singleline;

    // Razor directives are compiler instructions, never rendered text.
    private static readonly Regex Directive = new(
        @"^\s*@(?:inherits|implements|inject|using|page|layout|attribute|namespace|typeparam|" +
        @"rendermode|preservewhitespace|addTagHelper|model)\b.*$", Multi);

    private static readonly Regex RazorComment = new(@"@\*.*?\*@", Single);
    private static readonly Regex HtmlComment = new(@"<!--.*?-->", Single);
    private static readonly Regex StyleBlock = new(@"<style\b.*?</style>", Single | RegexOptions.IgnoreCase);
    private static readonly Regex ScriptBlock = new(@"<script\b.*?</script>", Single | RegexOptions.IgnoreCase);

    /// <summary>
    /// Elements whose content is literal machine text rather than prose, and whose CONTENT is
    /// therefore removed before counting (the elements themselves stay, so the surrounding sentence
    /// still counts as the run it is).
    /// </summary>
    /// <remarks>
    /// Added 2026-07-31 during REQ-UI-050's page tranche, as a correction to the DEFINITION of a
    /// user-visible string site rather than a relaxation of the bar. <c>&lt;code&gt;Docker:&lt;/code&gt;</c>
    /// and <c>&lt;code&gt;tcp://&lt;/code&gt;</c> are things the operator types verbatim; translating them
    /// would print an instruction that does not work, so counting them as untranslated text asks
    /// for a defect. This can only ever REMOVE hardcoded sites, never localized ones — no
    /// <c>Localizer</c> lookup sits inside one of these elements anywhere in the tree, and one that
    /// did would be a bug in its own right.
    /// </remarks>
    /// <remarks>
    /// 2026-08-01: <c>TypographyInlineCode</c> joined the list. It is TrBlazeUI's component for the
    /// <c>&lt;code&gt;</c> element and nothing else, and the only two things the app puts inside it
    /// are the environment-variable names <c>AppManager__ApiKey</c> and <c>AppManager__ApiSecret</c>
    /// on the setup wizard — which the operator types character for character.
    /// </remarks>
    private static readonly Regex LiteralElement = new(
        @"<(code|kbd|samp|pre|TypographyInlineCode)\b[^>]*>.*?</\1>", Single | RegexOptions.IgnoreCase);

    // A quoted attribute value may itself contain '>' — @onclick="@(() => Go(x))" does — so the
    // naive <[^>]*> ends the tag early and spills C# into the text stream as fake prose.
    private static readonly Regex Tag = new("<(?:\"[^\"]*\"|'[^']*'|[^>\"'])*>", Single);

    private static readonly Regex Control = new(
        @"@(?:else\s+if|if|foreach|for|while|switch|lock|using|do|try|catch|finally|else)\b" +
        @"\s*(?:\((?:[^()]|\([^()]*\))*\))?");

    private static readonly Regex Lookup = new(@"@?Localizer\s*\[(?:[^\[\]]|\[[^\]]*\])*\]");
    private static readonly Regex ParenExpression = new(@"@\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)");
    // Three levels of nested parentheses (matching ParenExpression) and a CHAIN of calls, not just
    // one at the end. Two levels was not enough for the ordinary shape
    // @string.Join(", ", xs.Take(10).Select(v => v.ToString("F4"))), and a single trailing call was
    // not enough for @row.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") — in both cases
    // the unconsumed tail stood in the text stream as a phantom English run.
    private const string CallArguments = @"(?:\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))?";

    private static readonly Regex MemberExpression = new(
        @"@[A-Za-z_]\w*" + CallArguments + @"(?:\.[A-Za-z_]\w*" + CallArguments + ")*");

    /// <summary>
    /// A C# declaration of a generic-typed local, e.g. <c>RenderFragment&lt;AttachmentChipRow&gt;
    /// attachmentChips =</c>, which appears in a <c>@{ }</c> block in markup.
    /// </summary>
    /// <remarks>
    /// Its type argument looks exactly like an HTML tag, so <see cref="Tag"/> eats it and leaves the
    /// type NAME behind as a phantom English run. Deliberately narrow — an uppercase type, an
    /// uppercase type argument, an identifier and an <c>=</c> — so that ordinary markup such as
    /// <c>Text&lt;b&gt;bold&lt;/b&gt;</c> cannot match it.
    /// </remarks>
    private static readonly Regex GenericDeclaration = new(
        @"\b[A-Z]\w*<[A-Z][\w\.,\s\?\[\]<>]*>\s+[A-Za-z_]\w*\s*=");

    private static readonly Regex Braces = new(@"[{}]");
    private static readonly Regex Letters = new(@"[A-Za-z]{2,}");

    // What is left of a razor block body is C#: a bare `else` (the `@` sits on the opening `@if`),
    // a `switch` label, a statement ending in a semicolon, a line ending in a lambda arrow, or
    // stray punctuation — including the bare `@` that opens a templated `@<div>` fragment.
    private static readonly Regex Statement = new(
        @"^\s*(?:else\b.*|case\b.*:|default\s*:|.*;|.*=>|[\[\]{}(),@]*)\s*$");

    private static readonly Regex Attribute = new("([A-Za-z_:@\\-]+)\\s*=\\s*\"([^\"]*)\"");

    private static readonly Regex ToastCall = new(
        @"\bToast(?:Service)?\s*\??\s*\.\s*(?:Error|Success|Show|Info|Warning|Warn)\s*\(([^;]*)", Single);

    private static readonly Regex StringLiteral = new("\"([^\"\\\\]*(?:\\\\.[^\"\\\\]*)*)\"");

    private static readonly Regex CodeBlockStart = new(@"@(?:code|functions)\s*\{");

    private static readonly Regex LineComment = new(@"//[^\n]*");
    private static readonly Regex BlockComment = new(@"/\*.*?\*/", Single);

    /// <summary>
    /// Names whose value is drawn on screen or read aloud. Deliberately EXCLUDES
    /// <c>Name</c> — that is <c>LucideIcon</c>'s icon id and HTML's form field name, neither of
    /// which anybody reads — and every routing, styling and binding attribute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>2026-08-01 — WIDENED, deliberately lowering the reported percentage.</b> REQ-UI-050's
    /// tranche 3 disclosed that this set was too narrow to be believed: <c>errorMessage</c>,
    /// <c>progressMessage</c>, <c>PickerTitle</c>, <c>catalogueError</c>, <c>promoError</c> and
    /// their kin are real user-visible strings, they were still English, and because nothing scored
    /// them the ratchet could not see them — so the headline figure OVERSTATED coverage. Three
    /// changes close that, and all three can only ever ADD hardcoded sites:
    /// </para>
    /// <list type="number">
    /// <item><c>error</c> joins the set, which is the word this codebase actually names its
    /// user-facing failure text with.</item>
    /// <item>A name now matches if it ENDS with one of these words as well as if it equals one, so
    /// <c>errorMessage</c>, <c>deactivateError</c> and <c>PickerTitle</c> score without the list
    /// having to grow a row per field. The suffix must be the tail of the name, so <c>HeaderClass</c>
    /// and <c>NameClass</c> — styling — still do not match.</item>
    /// <item>The match runs over the <c>@code</c> block as well as the markup. This is the narrow,
    /// deliberate exception to the "C# labels are out of the population" rule stated in the class
    /// remarks: that rule exists because ONE markup position fed by many branches is one site, and it
    /// still holds for branch tables such as <c>MainLayout</c>'s breadcrumb map. A named field
    /// assigned an English SENTENCE is a different thing — it is a distinct string a translator has
    /// to translate — and it reaches the user exactly as a markup literal does.</item>
    /// </list>
    /// <para>
    /// The NUMERATOR is untouched by all of this: <c>Localizer</c> lookups are still counted in
    /// markup and in toast calls only, so the 45 / 117 / 942 / 1,709 series stays directly
    /// comparable. Localizing one of these fields therefore removes it from the population rather
    /// than moving it into the numerator, which is the conservative direction.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> TextAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "text", "placeholder", "label", "alt", "aria-label", "description",
        "header", "message", "heading", "subtitle", "hint", "tooltip", "summary",
        "confirmtext", "canceltext", "emptytext", "buttontext", "helptext", "legend",
        "aria-description", "arialabel", "ariadescription",

        // 2026-08-01: the word this codebase names user-facing failure text with — `loadError`,
        // `promoError`, `deactivateError`, `currentPasswordError`. See the remarks above.
        "error",
    };

    /// <summary>
    /// Decides whether a name carries text a user reads.
    /// </summary>
    /// <param name="name">The attribute or field name.</param>
    /// <returns><see langword="true"/> when its value is user-visible text.</returns>
    /// <remarks>
    /// Exact match first, then suffix — see the remarks on <see cref="TextAttributes"/> for why the
    /// suffix rule exists and why it cannot match a styling name.
    /// </remarks>

    /// <summary>
    /// Names the suffix rule must never claim, whatever they end in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Context</c> ends in "text" and is Blazor's own directive attribute naming the loop
    /// variable of a templated component — <c>Context="row"</c> is a compiler instruction that
    /// renders nothing, and there are 53 of them across the converted pages. Counting them made ten
    /// already-localized screens fail <c>LocalizedFilesNeverRegainAHardcodedString</c> for text that
    /// does not exist, which is the failure mode the counter's own remarks warn about: a red build
    /// nobody can fix by translating anything teaches people to edit the threshold.
    /// </para>
    /// <para>
    /// This is the same kind of carve-out as <see cref="TextAttributes"/>'s deliberate omission of
    /// <c>Name</c>, and it can only ever REMOVE hardcoded sites — the numerator is untouched, so the
    /// ratchet's history stays comparable. Added 2026-08-01 by REQ-UI-051.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> NeverText = new(StringComparer.OrdinalIgnoreCase)
    {
        "Context",
    };

    /// <summary>Gets whether an attribute or field name carries text a user reads.</summary>
    /// <param name="name">The attribute or member name.</param>
    /// <returns>True when its literal value would be read by a user.</returns>
    private static bool IsTextBearing(string name)
    {
        if (NeverText.Contains(name))
        {
            return false;
        }

        if (TextAttributes.Contains(name))
        {
            return true;
        }

        foreach (var word in TextAttributes)
        {
            // A one-word name is already covered by the exact match above; requiring the suffix to
            // be strictly shorter than the name keeps this from re-testing the same thing.
            if (name.Length <= word.Length || !name.EndsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The suffix has to start a WORD, or `Context` — Blazor's RenderFragment variable, which
            // no user ever sees — matches "text" and every DataTable column in the app is reported
            // as untranslated English. Caught by running the widened counter before trusting it.
            var start = name.Length - word.Length;
            if (char.IsUpper(name[start]) || name[start - 1] is '-' or '_' or '.')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tokens that are NOT English prose and that the coding standards forbid translating: product
    /// and brand names, protocol and header names, and literal input formats the user types
    /// verbatim. A text run or attribute value consisting only of one of these is not a hardcoded
    /// site, because there is nothing to translate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one place the counter takes somebody's word for it, so it is deliberately an
    /// EXACT-MATCH list of whole values rather than a pattern, it is short, and
    /// <c>StringCoverageTests.TheUntranslatableListStaysSmallAndInUse</c> caps its size and
    /// requires every entry to still appear in the component tree. Adding to it is the same
    /// weight of decision as adding a resource key, and the reviewer's question is the same one
    /// every time: would a Hindi speaker be worse off if this were translated? For
    /// <c>Authorization</c> (the HTTP header the user must literally type) and <c>yyyy-mm-dd</c>
    /// (the format the field parses) the answer is yes — translating them prints an instruction
    /// that does not work.
    /// </para>
    /// <para>
    /// Note what is NOT here: nothing that is a sentence, a label, a button, or any word a user
    /// reads for meaning rather than copies. "Refresh" is not a brand.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlySet<string> Untranslatable = new HashSet<string>(StringComparer.Ordinal)
    {
        // Brand and product names shown as themselves (Select options, emphasised in a sentence).
        "GitHub", "GitLab", "Gmail", "Microsoft 365", "Qdrant",

        // Literal input formats and protocol nouns the user types or looks for verbatim.
        "owner/repository", "group/subgroup/project", "yyyy-mm-dd", "/wiki",
        "localhost", "Authorization",

        // 2026-08-01, REQ-UI-050 tranche 4.
        // The product's own name, rendered as itself as the window title of "/" — the same case as
        // GitHub above. Every other page title reads "<something> — TechieDesk" and is translated;
        // it is only the bare name standing alone that has nothing to translate.
        "TechieDesk",

        // The licence ENTITLEMENT CODE, drawn as a badge one line below `Feature="WHITE_LABEL"` on
        // the same component. Translating the badge would decouple it from the code it names.
        "WHITE_LABEL",

        // Example values the user overtypes — the same case as owner/repository above.
        "admin@company.com", "https://appmanager.example.com",
    };

    /// <summary>
    /// Gets the absolute path of <c>apps/TechieDesk/Components</c>.
    /// </summary>
    /// <returns>The components directory.</returns>
    /// <exception cref="InvalidOperationException">
    /// The repository root could not be found from the test output directory.
    /// </exception>
    /// <remarks>
    /// Anchored on <c>TechieRag.slnx</c> rather than on a relative hop count, and it THROWS rather
    /// than returning an empty scan. The MAUI head cannot be referenced from this net10.0 test
    /// project, so reading the components off disk is the only way to measure them — and a
    /// path-walk that silently finds nothing would turn this whole ratchet into a test that passes
    /// forever, which is the exact failure mode the coverage number exists to expose.
    /// </remarks>
    public static string FindComponentsRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechieRag.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not find TechieRag.slnx above " + AppContext.BaseDirectory +
                ", so the razor components could not be located.");
        }

        var components = Path.Combine(directory.FullName, "apps", "TechieDesk", "Components");
        if (!Directory.Exists(components))
        {
            throw new InvalidOperationException($"'{components}' does not exist.");
        }

        return components;
    }

    /// <summary>Measures every razor component under <c>Components/</c>.</summary>
    /// <returns>One row per component, in path order.</returns>
    /// <exception cref="InvalidOperationException">Implausibly few components were found.</exception>
    public static IReadOnlyList<RazorFileCoverage> Scan()
    {
        var root = FindComponentsRoot();
        var files = Directory.GetFiles(root, "*.razor", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length < 30)
        {
            throw new InvalidOperationException(
                $"Only {files.Length} .razor files were found under '{root}'. The app has far more, " +
                "so the scan is looking in the wrong place and every number below would be a lie.");
        }

        return files.Select(path => Measure(path, Path.GetRelativePath(root, path))).ToArray();
    }

    /// <summary>
    /// Collects every resource key the razor components name as a literal.
    /// </summary>
    /// <returns>The distinct keys, in ordinal order.</returns>
    /// <remarks>
    /// <para>
    /// Only a LITERAL first argument is collected: <c>Localizer["NavChat"]</c> is a key a screen
    /// asks for and can therefore be asserted against the resources, whereas
    /// <c>Localizer[keyVariable]</c> or <c>Localizer["Accent" + name]</c> is composed at runtime and
    /// cannot be. The composed ones are covered by the targeted tests that rebuild the same names
    /// (<c>NamesEveryAccentInEveryLanguage</c>, <c>NamesEveryThemeOptionInEveryLanguage</c>).
    /// </para>
    /// <para>
    /// This replaces maintaining a hand-written list of several hundred keys, which the volume of
    /// REQ-UI-050's page tranche made untenable — a list that long stops being read and starts being
    /// appended to without checking. The objection to scraping was that a relative path walk passes
    /// silently when the path is wrong; that objection is answered by
    /// <see cref="FindComponentsRoot"/>, which throws, and by the caller asserting a plausible count.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> KeysRequestedByComponents()
    {
        var root = FindComponentsRoot();
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(root, "*.razor", SearchOption.AllDirectories))
        {
            foreach (Match match in LiteralLookup.Matches(File.ReadAllText(path)))
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        return keys.ToArray();
    }

    private static readonly Regex LiteralLookup = new("\\bLocalizer\\s*\\[\\s*\"([^\"]+)\"");

    /// <summary>
    /// Counts the English literals assigned to a text-bearing name in one stream of source.
    /// </summary>
    /// <param name="source">Preprocessed markup, or a preprocessed <c>@code</c> block.</param>
    /// <returns>The number of hardcoded, user-visible sites found.</returns>
    /// <remarks>
    /// A value that begins with <c>@</c> is an expression, not a literal, so it is already carrying
    /// whatever the component chose to put there — including a <c>Localizer</c> lookup.
    /// </remarks>
    private static int CountTextBearingLiterals(string source)
    {
        var found = 0;

        foreach (Match match in Attribute.Matches(source))
        {
            var name = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim();
            if (name.StartsWith('@') || !IsTextBearing(name))
            {
                continue;
            }

            if (value.Length == 0 || value.StartsWith('@') || !Letters.IsMatch(value))
            {
                continue;
            }

            if (Untranslatable.Contains(value))
            {
                continue;
            }

            found++;
        }

        return found;
    }

    /// <summary>Measures one component.</summary>
    /// <param name="path">The component's absolute path.</param>
    /// <param name="relativePath">Its path below <c>Components/</c>, used for reporting.</param>
    /// <returns>The component's coverage.</returns>
    public static RazorFileCoverage Measure(string path, string relativePath) =>
        MeasureSource(File.ReadAllText(path), relativePath);

    /// <summary>
    /// Measures razor SOURCE rather than a file on disk.
    /// </summary>
    /// <param name="source">The razor text to measure.</param>
    /// <param name="relativePath">The path to report the result under.</param>
    /// <returns>The coverage of that text.</returns>
    /// <remarks>
    /// Added by REQ-UI-051 so <see cref="CodeBlockStringCoverage"/> can run these same, tuned
    /// heuristics over the markup that lives INSIDE a <c>@code</c> block — a <c>RenderFragment</c>
    /// is razor markup by any other name, and it was invisible to <see cref="Measure"/> precisely
    /// because <see cref="Split"/> hands the whole code block to the toast scanner and no further.
    /// Behaviour for a whole file is unchanged: <see cref="Measure"/> is now a one-line call here.
    /// </remarks>
    public static RazorFileCoverage MeasureSource(string source, string relativePath)
    {
        var (markup, code) = Split(source);

        markup = ScriptBlock.Replace(
            StyleBlock.Replace(
                HtmlComment.Replace(RazorComment.Replace(markup, " "), " "), " "), " ");
        markup = Directive.Replace(markup, " ");
        markup = LiteralElement.Replace(markup, match => $"<{match.Groups[1].Value}> </{match.Groups[1].Value}>");
        markup = GenericDeclaration.Replace(markup, " = ");
        code = BlockComment.Replace(LineComment.Replace(code, " "), " ");

        var localized = Lookup.Matches(markup).Count;

        // Markup AND code-behind — see the remarks on TextAttributes for why the code block is in
        // scope for this one measurement and not for the runs below.
        var attributes = CountTextBearingLiterals(markup) + CountTextBearingLiterals(code);

        var runs = 0;
        foreach (var chunk in Tag.Replace(markup, "\u0000").Split('\u0000'))
        {
            var text = Braces.Replace(
                MemberExpression.Replace(
                    ParenExpression.Replace(
                        Control.Replace(Lookup.Replace(chunk, " "), " "), " "), " "), " ");

            var prose = string.Join(
                '\n',
                text.Split('\n').Where(line => !Statement.IsMatch(line))).Trim();

            if (prose.Length > 0 && Letters.IsMatch(prose) && !Untranslatable.Contains(prose))
            {
                runs++;
            }
        }

        var toasts = 0;
        foreach (Match call in ToastCall.Matches(code))
        {
            var arguments = call.Groups[1].Value;
            localized += Lookup.Matches(arguments).Count;

            // A resource KEY is not text anybody reads, so drop the lookups before counting literals.
            arguments = Lookup.Replace(arguments, " ");
            toasts += StringLiteral.Matches(arguments)
                .Count(literal => Letters.IsMatch(literal.Groups[1].Value));
        }

        return new RazorFileCoverage(relativePath.Replace('\\', '/'), localized, runs, attributes, toasts);
    }

    /// <summary>Splits a component into its markup and its trailing <c>@code</c> block.</summary>
    private static (string Markup, string Code) Split(string source)
    {
        var start = CodeBlockStart.Match(source);
        if (!start.Success)
        {
            return (source, string.Empty);
        }

        var brace = source.IndexOf('{', start.Index);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return (source[..start.Index] + source[(i + 1)..], source[brace..(i + 1)]);
                }
            }
        }

        return (source[..start.Index], source[brace..]);
    }
}
