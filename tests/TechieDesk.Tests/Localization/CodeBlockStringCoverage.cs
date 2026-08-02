using System.Text.RegularExpressions;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// One .razor component's UNMEASURED user-visible text: the strings its <c>@code</c> block builds
/// (REQ-UI-051 / BRD-91).
/// </summary>
/// <param name="RelativePath">The component's path below <c>Components/</c>.</param>
/// <param name="ProseLiterals">English sentences written as C# literals inside the code block.</param>
/// <param name="FragmentRuns">Literal prose inside a <c>RenderFragment</c>'s markup.</param>
/// <param name="FragmentAttributes">Literal text-bearing attributes inside a <c>RenderFragment</c>.</param>
public sealed record CodeBlockCoverage(
    string RelativePath,
    int ProseLiterals,
    int FragmentRuns,
    int FragmentAttributes)
{
    /// <summary>Gets every user-visible English site found inside the code block.</summary>
    public int Hardcoded => ProseLiterals + FragmentRuns + FragmentAttributes;
}

/// <summary>
/// Counts the user-visible English a component builds inside its <c>@code</c> block (REQ-UI-051 /
/// BRD-91) — the population <see cref="RazorStringCoverage"/> does not measure.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="RazorStringCoverage"/> measures MARKUP by documented design:
/// a label that lives as a C# literal behind one markup position is one site however many branches
/// feed it, and counting the branches would let a refactor move the percentage without translating
/// anything. That is a defensible definition of the RATIO. Its consequence was not defensible: a
/// page could report "101 hardcoded sites → 0" and still render English, because
/// <c>ComposerPlaceholder()</c>, <c>OutcomeLabel(report)</c> and a whole <c>RenderFragment</c> full
/// of <c>&lt;FieldLabel&gt;Source&lt;/FieldLabel&gt;</c> all sit inside the code block, and the code
/// block is handed to the toast scanner and otherwise thrown away. The chat composer's placeholder
/// — the most-read string in the product — was English on a Hindi install with every test green.
/// </para>
/// <para>
/// <b>What this measures.</b> Two things, both inside the <c>@code</c> block:
/// </para>
/// <list type="number">
/// <item><b>Prose literals</b> — a C# string literal that reads as a sentence fragment: it contains
/// a space and at least two runs of two or more letters, once interpolation holes are removed.
/// <c>"Whole workspace"</c> counts; <c>"support-new-issue-paste"</c>, <c>"Cosine"</c> and
/// <c>"{sign}{magnitude} {code}"</c> do not.</item>
/// <item><b>Fragment markup</b> — the razor inside a <c>RenderFragment</c>, measured by
/// <see cref="RazorStringCoverage.MeasureSource"/> so it is scored by exactly the same, already
/// tuned, heuristics as the rest of the app's markup.</item>
/// </list>
/// <para>
/// <b>What this does NOT cover, stated plainly.</b>
/// </para>
/// <list type="bullet">
/// <item>A one-word user-visible label. <c>"Failed"</c> written as a C# literal is invisible here,
/// because requiring two words is what keeps every tool name, wire code, CSS class, element id and
/// enum token out of the count. One-word labels are real defects and this test will not find them.</item>
/// <item>English that reaches the user from OUTSIDE the component — a service, a model, or the
/// server. <c>SkillCatalog</c>'s English is invisible to a scan of the razor tree; REQ-UI-051 dealt
/// with it by mapping those values onto resource keys in the pages, and nothing here would notice
/// if a future service grew a new English label.</item>
/// <item>Whether the Hindi is any GOOD. Like every other test here, this one counts English that
/// was never routed through the localizer; it cannot see a translation that is wrong.</item>
/// <item>A literal built by concatenation at runtime from single words, or read from a constant in
/// another file.</item>
/// </list>
/// <para>
/// It is deliberately conservative in the direction of UNDER-counting, which is the opposite choice
/// from <see cref="RazorStringCoverage"/>. That counter reports a percentage nobody has to act on
/// line by line; this one is a ZERO-tolerance gate on the localized-file registry, so a false
/// positive here blocks an unrelated change and teaches people to widen the exemption list. The
/// price is the blind spots listed above, and they are listed rather than hidden.
/// </para>
/// </remarks>
public static class CodeBlockStringCoverage
{
    private static readonly Regex CodeBlockStart = new(@"@(?:code|functions)\s*\{");
    private static readonly Regex InterpolationHole = new(@"\{[^{}]*\}");
    private static readonly Regex LetterRun = new("[A-Za-z]{2,}");

    /// <summary>The lambda binding that opens a <c>RenderFragment</c> body.</summary>
    private static readonly Regex FragmentLambda = new(@"__builder\s*=>\s*\{");

    /// <summary>
    /// Calls whose string arguments are read by a machine, not by a person.
    /// </summary>
    /// <remarks>
    /// A logger message TEMPLATE is structured-logging syntax and is read in a log viewer;
    /// <c>ToString</c> and <c>ParseExact</c> take .NET format strings, which are a contract with the
    /// runtime; JS interop takes a function name. Translating any of them breaks the thing rather
    /// than localizing it. Matched on the line, which is coarse but is what keeps this rule short
    /// enough to be read.
    /// </remarks>
    private static readonly Regex MachineFacingCall = new(
        @"Logger\s*\.\s*Log|\.\s*ToString\s*\(|ParseExact|nameof\s*\(|Invoke(?:Void)?Async\s*[\(<]");

    /// <summary>
    /// A <c>const string</c> whose name ends in <c>Sentinel</c>: a value that is PERSISTED or
    /// compared, and therefore has to stay culture-invariant.
    /// </summary>
    /// <remarks>
    /// REQ-UI-051 introduced the convention with <c>WorkspaceChat.UntitledThreadSentinel</c>: the
    /// thread title written to the conversation store stays English on disk, and the page maps it to
    /// a resource key for display. Naming the field is how the author declares that, and it is
    /// checked by review — the suffix is a claim, not a proof.
    /// </remarks>
    private static readonly Regex InvariantSentinel = new(@"const\s+string\s+\w*Sentinel\b");

    /// <summary>
    /// Multi-word values that are NOT English UI prose: brand names shown as themselves, and text
    /// addressed to the language model rather than to the reader.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of <see cref="RazorStringCoverage.Untranslatable"/>, kept SEPARATE from it on
    /// purpose. That list is an input to the coverage percentage every cluster's build depends on;
    /// this one gates a handful of files, and four clusters are writing UI at once. A shared list is
    /// a shared blast radius for no benefit.
    /// </para>
    /// <para>
    /// An entry here is a claim that the string is not UI, and
    /// <c>CodeBlockCoverageTests.TheMachineTextListStaysSmallAndInUse</c> is what keeps the claim
    /// honest: the list is capped, and an entry no component still contains has to be deleted. The
    /// prompt fragments are the interesting case — they are sent to the model as instructions, and
    /// translating a system prompt changes what the model does rather than what the user reads.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlySet<string> MachineText = new HashSet<string>(StringComparer.Ordinal)
    {
        // Brand names, rendered as themselves in a provider list.
        "LM Studio", "Azure AI Foundry", "Google Gemini",

        // Brand names, rendered as themselves in a provider list (REQ-UI-050 tranche 4).
        "Anthropic Claude",

        // A filesystem path the operator reads and types, printed on the Updates screen. IsProse
        // already exempts a path beginning with '/'; this one begins with '~/'.
        "~/Library/Application Support/TechieDesk",

        // Addressed to the model, not to the user.
        "Say 'hello' in one word.",
        "Answer only from the passages returned by your tools. If they do not answer the ",
        "question, say plainly that the documents do not cover it rather than reasoning from ",
        "general knowledge.",

        // REQ-UI-050 tranche 4 — the RAG and structured-output prompts the two chat surfaces send.
        // Every one of these is read by the model and never drawn on screen; translating them would
        // change what the model is asked to do rather than what the user is shown. Written out
        // exactly as the lexer reads them, so the escapes are the source's, not prose.
        "You are a helpful assistant. Answer the user's question.",
        "You are a helpful assistant.",
        "You are a helpful assistant. Answer the user's question using only the retrieved context below. If the answer is not contained in the context, say you don't know.\\n\\n--- Retrieved Context ---\\n{contextText}\\n--- End Context ---",
        "Analyze the sentiment and respond with JSON: {{\\\"sentiment\\\": \\\"positive|negative|neutral\\\", \\\"confidence\\\": 0.0-1.0, \\\"explanation\\\": \\\"...\\\"}}\\n\\n{structuredPrompt}",
        "Create a weather forecast and respond with JSON: {{\\\"city\\\": \\\"...\\\", \\\"temperature\\\": 0, \\\"condition\\\": \\\"...\\\", \\\"humidity\\\": 0}}\\n\\n{structuredPrompt}",
        "Summarize the book and respond with JSON: {{\\\"title\\\": \\\"...\\\", \\\"author\\\": \\\"...\\\", \\\"summary\\\": \\\"...\\\", \\\"rating\\\": 0.0-5.0}}\\n\\n{structuredPrompt}",
    };

    /// <summary>Measures every razor component under <c>Components/</c>.</summary>
    /// <returns>One row per component that HAS a code block, in path order.</returns>
    public static IReadOnlyList<CodeBlockCoverage> Scan()
    {
        var root = RazorStringCoverage.FindComponentsRoot();
        var files = Directory.GetFiles(root, "*.razor", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length < 30)
        {
            throw new InvalidOperationException(
                $"Only {files.Length} .razor files were found under '{root}', so the scan is " +
                "looking in the wrong place and every number below would be a lie.");
        }

        return files
            .Select(path => Measure(
                File.ReadAllText(path),
                Path.GetRelativePath(root, path).Replace('\\', '/')))
            .Where(row => row is not null)
            .Select(row => row!)
            .ToArray();
    }

    /// <summary>Measures one component's code block.</summary>
    /// <param name="source">The component's full razor text.</param>
    /// <param name="relativePath">Its path below <c>Components/</c>.</param>
    /// <returns>The coverage row, or null when the component has no code block.</returns>
    /// <remarks>
    /// The block is cut into RAZOR TEMPLATE regions — a <c>RenderFragment</c> body, which the razor
    /// compiler marks by lambda-binding <c>__builder</c> — and everything else, which is C#. Each
    /// half is then measured by the tool that fits it. Running the markup counter over plain C#
    /// instead reported a dozen phantom "text runs" per page from lambda arrows and switch arms,
    /// and a gate that cries wolf is a gate somebody deletes.
    /// </remarks>
    public static CodeBlockCoverage? Measure(string source, string relativePath)
    {
        var body = ExtractCodeBlock(source);
        if (body is null)
        {
            return null;
        }

        var fragments = FragmentRegions(body);

        var runs = 0;
        var attributes = 0;
        foreach (var (start, end) in fragments)
        {
            var measured = RazorStringCoverage.MeasureSource(body[start..end], relativePath);
            runs += measured.HardcodedRuns;
            attributes += measured.HardcodedAttributes;
        }

        return new CodeBlockCoverage(relativePath, ProseLiterals(body, fragments).Count, runs, attributes);
    }

    /// <summary>
    /// Finds the razor template regions inside a code block.
    /// </summary>
    /// <param name="code">The code block body.</param>
    /// <returns>The half-open spans of every <c>RenderFragment</c> body found, in order.</returns>
    /// <remarks>
    /// <c>__builder</c> is the parameter the razor compiler binds a <c>RenderFragment</c> lambda to,
    /// so <c>=&gt; __builder =&gt; { ... }</c> is the one syntactic marker that a block of markup is
    /// about to start. Anchoring on it keeps this from guessing: a generic such as
    /// <c>Func&lt;TValue, string&gt;</c> opens with the same two characters as a tag, and a
    /// heuristic that treated every <c>&lt;</c> as markup would carve C# into nonsense.
    /// </remarks>
    private static IReadOnlyList<(int Start, int End)> FragmentRegions(string code)
    {
        var regions = new List<(int, int)>();

        foreach (Match marker in FragmentLambda.Matches(code))
        {
            var open = code.IndexOf('{', marker.Index + marker.Length - 1);
            if (open < 0)
            {
                continue;
            }

            var end = MatchingBrace(code, open);
            if (end > open)
            {
                regions.Add((open + 1, end));
            }
        }

        return regions;
    }

    /// <summary>
    /// Extracts the body of the component's <c>@code</c> block.
    /// </summary>
    /// <param name="source">The component's full razor text.</param>
    /// <returns>The block body, or null when there is no code block.</returns>
    /// <remarks>
    /// Brace-matches while SKIPPING string, verbatim-string and character literals, so a brace
    /// inside <c>$"{count} file(s)"</c> or <c>"}"</c> cannot end the block early and silently hide
    /// the rest of it from this scan. That is the one place this differs from
    /// <c>RazorStringCoverage.Split</c>, which does not need the precision.
    /// </remarks>
    public static string? ExtractCodeBlock(string source)
    {
        var start = CodeBlockStart.Match(source);
        if (!start.Success)
        {
            return null;
        }

        var open = source.IndexOf('{', start.Index);
        var depth = 0;

        for (var i = open; i < source.Length; i++)
        {
            var c = source[i];

            if (c == '@' && i + 1 < source.Length && source[i + 1] == '"')
            {
                i = SkipVerbatimString(source, i + 1);
                continue;
            }

            if (c == '"')
            {
                i = SkipQuoted(source, i, '"');
                continue;
            }

            if (c == '\'')
            {
                i = SkipQuoted(source, i, '\'');
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[(open + 1)..i];
                }
            }
        }

        return source[(open + 1)..];
    }

    /// <summary>
    /// Finds the English sentence fragments a code block writes as C# literals.
    /// </summary>
    /// <param name="code">The code block body.</param>
    /// <param name="fragments">The razor template regions to skip.</param>
    /// <returns>The offending literals, in source order.</returns>
    /// <remarks>
    /// Literals inside a template region are ATTRIBUTES and are already scored by the fragment
    /// measure; counting them here as well would report one defect twice.
    /// </remarks>
    private static IReadOnlyList<string> ProseLiterals(
        string code, IReadOnlyList<(int Start, int End)> fragments)
    {
        var found = new List<string>();
        var literals = Literals(code).ToArray();
        var masked = Mask(code, literals);

        foreach (var (index, value) in literals)
        {
            if (fragments.Any(region => index >= region.Start && index < region.End))
            {
                continue;
            }

            if (!IsProse(value)
                || MachineText.Contains(value)
                || RazorStringCoverage.Untranslatable.Contains(value))
            {
                continue;
            }

            var statement = StatementBefore(masked, index);
            if (MachineFacingCall.IsMatch(statement) || InvariantSentinel.IsMatch(statement))
            {
                continue;
            }

            found.Add(value);
        }

        return found;
    }

    /// <summary>
    /// Lexes the C# string literals out of a code block.
    /// </summary>
    /// <param name="code">The code block body.</param>
    /// <returns>The index and text of every string literal, comments excluded.</returns>
    /// <remarks>
    /// A single pass rather than a stack of regexes, because the regexes got this wrong in the way
    /// that matters: stripping <c>//</c> comments first ate the <c>//</c> inside
    /// <c>"http://localhost:11434"</c>, unbalanced every quote after it, and turned the rest of the
    /// file into garbage "literals" the scan then reported. A lexer cannot make that mistake — it
    /// knows whether it is inside a string before it decides what a slash means.
    /// </remarks>
    private static IEnumerable<(int Index, string Value)> Literals(string code)
    {
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];

            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                var newline = code.IndexOf('\n', i);
                i = newline < 0 ? code.Length : newline;
                continue;
            }

            if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                var close = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? code.Length : close + 1;
                continue;
            }

            if (c == '@' && i + 1 < code.Length && code[i + 1] == '*')
            {
                var close = code.IndexOf("*@", i + 2, StringComparison.Ordinal);
                i = close < 0 ? code.Length : close + 1;
                continue;
            }

            if (c == '@' && i + 1 < code.Length && code[i + 1] == '"')
            {
                var close = SkipVerbatimString(code, i + 1);
                yield return (i + 2, code[(i + 2)..Math.Min(close, code.Length)]);
                i = close;
                continue;
            }

            if (c == '\'')
            {
                i = SkipQuoted(code, i, '\'');
                continue;
            }

            if (c != '"')
            {
                continue;
            }

            var end = SkipQuoted(code, i, '"');
            yield return (i + 1, code[(i + 1)..Math.Min(end, code.Length)]);
            i = end;
        }
    }

    /// <summary>
    /// Blanks out the INSIDE of every string literal, keeping every index the same.
    /// </summary>
    /// <param name="code">The code block body.</param>
    /// <param name="literals">The literals lexed out of it.</param>
    /// <returns>The same text with literal contents replaced by spaces.</returns>
    /// <remarks>
    /// So that <see cref="StatementBefore"/> cannot be stopped by punctuation that is part of a
    /// SENTENCE. The live example: a logger template reading "…applied successfully; the page now
    /// reflects " has a semicolon in it, and walking back over the raw text stopped there instead of
    /// at <c>Logger.LogInformation(</c> — which reported the second half of a log message as
    /// untranslated UI while correctly ignoring the first.
    /// </remarks>
    private static string Mask(string code, IReadOnlyList<(int Index, string Value)> literals)
    {
        var masked = code.ToCharArray();

        foreach (var (index, value) in literals)
        {
            for (var i = index; i < index + value.Length && i < masked.Length; i++)
            {
                masked[i] = ' ';
            }
        }

        return new string(masked);
    }

    /// <summary>Gets the statement a literal sits in, back to the previous statement boundary.</summary>
    /// <param name="code">The code block body.</param>
    /// <param name="index">Where the literal starts.</param>
    /// <returns>The text from the last <c>;</c>, <c>{</c> or <c>}</c> up to the literal.</returns>
    /// <remarks>
    /// The enclosing STATEMENT rather than the enclosing LINE, because a logger call routinely puts
    /// its message template on the line after <c>Logger.LogInformation(</c>, and a line-scoped rule
    /// reported every one of those as untranslated UI.
    /// </remarks>
    private static string StatementBefore(string code, int index)
    {
        var start = index;
        while (start > 0 && code[start - 1] is not (';' or '{' or '}'))
        {
            start--;
        }

        return code[start..index];
    }

    /// <summary>Gets whether a literal reads as English prose rather than as a machine value.</summary>
    /// <param name="value">The literal's text, escapes and all.</param>
    /// <returns>True when a user would read it as words.</returns>
    /// <remarks>
    /// Three rejections carry the rule. A value with <c>=</c> or <c>://</c> is a connection string
    /// or a URL — <c>"Data Source=techierag.db"</c> is the live example, and translating one
    /// produces a setting that does not work. A value starting with <c>/</c> is a route
    /// (<c>"/workspace/{Slug}/settings"</c>). And prose needs two WORDS, counted after interpolation
    /// holes are removed, which is what separates <c>"Whole workspace"</c> from
    /// <c>"qdrant-host.lan:{port}"</c> and from every element id in the tree.
    /// </remarks>
    private static bool IsProse(string value)
    {
        if (value.Contains('=', StringComparison.Ordinal)
            || value.Contains("://", StringComparison.Ordinal)
            || value.StartsWith('/'))
        {
            return false;
        }

        var text = InterpolationHole.Replace(value, " ")
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace("\\t", " ", StringComparison.Ordinal)
            .Replace("\\r", " ", StringComparison.Ordinal);

        var words = text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => LetterRun.IsMatch(token))
            .ToArray();

        return words.Length >= 2 && !IsUtilityClassList(words);
    }

    /// <summary>
    /// Gets whether a run of words is a CSS utility-class list rather than a sentence.
    /// </summary>
    /// <param name="words">The literal's word tokens.</param>
    /// <returns>True when it reads as Tailwind classes.</returns>
    /// <remarks>
    /// <para>
    /// <c>"flex h-10 w-full rounded-md border border-input bg-background px-3 py-2"</c> is eight
    /// words by any word-counting rule and is not English. The auth screens hold the classes
    /// TrBlazeUI's <c>Input</c> emits in a <c>const string</c> so a plain input matches the rest of
    /// the app, and without this rule every one of those constants failed the gate.
    /// </para>
    /// <para>
    /// The test is deliberately narrow: all lower case, at least three words, and at least HALF of
    /// them carrying a <c>-</c> or a <c>:</c>. English prose reaches one hyphenated word in a
    /// sentence, not half of them — "read-only queries against a configured database" is one in six
    /// and stays prose.
    /// </para>
    /// </remarks>
    private static bool IsUtilityClassList(IReadOnlyList<string> words)
    {
        if (words.Count < 3 || words.Any(word => word.Any(char.IsUpper)))
        {
            return false;
        }

        var utility = words.Count(word =>
            word.Contains('-', StringComparison.Ordinal) || word.Contains(':', StringComparison.Ordinal));

        return utility * 2 >= words.Count;
    }

    /// <summary>Gets the index of the brace matching the one at <paramref name="open"/>.</summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="open">The index of the opening brace.</param>
    /// <returns>The index of the matching close, or -1.</returns>
    private static int MatchingBrace(string text, int open)
    {
        var depth = 0;

        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
            {
                i = SkipVerbatimString(text, i + 1);
                continue;
            }

            if (c is '"' or '\'')
            {
                i = SkipQuoted(text, i, c);
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Gets the index of the closing quote of a literal, honouring backslash escapes.</summary>
    private static int SkipQuoted(string text, int openIndex, char quote)
    {
        for (var i = openIndex + 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }

            if (text[i] == quote || text[i] == '\n')
            {
                return i;
            }
        }

        return text.Length - 1;
    }

    /// <summary>Gets the index of the closing quote of a verbatim string, where "" is one quote.</summary>
    private static int SkipVerbatimString(string text, int openIndex)
    {
        for (var i = openIndex + 1; i < text.Length; i++)
        {
            if (text[i] != '"')
            {
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '"')
            {
                i++;
                continue;
            }

            return i;
        }

        return text.Length - 1;
    }

    /// <summary>Renders a row for a failure message.</summary>
    /// <param name="row">The row to describe.</param>
    /// <returns>A single line naming what was found and where.</returns>
    public static string Describe(CodeBlockCoverage row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return $"{row.RelativePath}: {row.Hardcoded} site(s) — {row.ProseLiterals} C# prose " +
               $"literal(s), {row.FragmentRuns} fragment text run(s), " +
               $"{row.FragmentAttributes} fragment attribute(s)";
    }

    /// <summary>Lists the prose literals a component's code block still carries.</summary>
    /// <param name="source">The component's full razor text.</param>
    /// <returns>The offending literals, for a failure message.</returns>
    /// <remarks>
    /// A count tells a builder there is a problem; the strings tell them which one, which is the
    /// difference between a test that gets fixed and a test that gets suppressed.
    /// </remarks>
    public static IReadOnlyList<string> ProseLiteralsIn(string source)
    {
        var body = ExtractCodeBlock(source);
        return body is null ? [] : ProseLiterals(body, FragmentRegions(body));
    }
}
