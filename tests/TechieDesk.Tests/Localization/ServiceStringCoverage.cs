using System.Text.RegularExpressions;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// One service file's English prose literals — the population BOTH razor counters are structurally
/// blind to (REQ-UI-051 / BRD-91).
/// </summary>
/// <param name="RelativePath">The file's path below <c>Services/</c>.</param>
/// <param name="Literals">The English sentence fragments it writes as C# literals, in source order.</param>
public sealed record ServiceFileCoverage(string RelativePath, IReadOnlyList<string> Literals)
{
    /// <summary>Gets how many candidate user-visible English sites the file carries.</summary>
    public int Count => Literals.Count;
}

/// <summary>
/// Counts the English prose built in the SERVICE layer — <c>apps/TechieDesk.Core/Services/</c> —
/// which is where user-visible English survived after app-wide markup localization reached 100%
/// (REQ-UI-051 / BRD-91).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists, and why neither existing counter could have caught it.</b>
/// <see cref="RazorStringCoverage"/> measures MARKUP; <see cref="CodeBlockStringCoverage"/> measures
/// the <c>@code</c> blocks of registry components. Both scan the razor tree, and both said so in
/// their own remarks: <i>"English that reaches the user from OUTSIDE the component — a service, a
/// model, or the server … nothing here would notice if a future service grew a new English label."</i>
/// On 2026-08-01 a live Hindi install rendered nine artefact names and nine descriptions in English
/// at <c>/settings/data</c>, and every localization test in the repository was green, because
/// <c>DataStorageInspector</c> is a static class in <c>Services/Storage/</c> and neither counter
/// can see it. This is the counter for that population.
/// </para>
/// <para>
/// <b>What it measures.</b> One thing: a C# string literal in a file under <c>Services/</c> that
/// reads as English prose — it survives <see cref="IsProse"/>, which is the same discipline
/// <see cref="CodeBlockStringCoverage"/> arrived at after being wrong twice (two words minimum once
/// interpolation holes are removed; no <c>=</c>, no <c>://</c>, no leading <c>/</c>; not a Tailwind
/// class list). The literals are lexed rather than regexed out, for the same reason: stripping
/// <c>//</c> comments with a regex eats the slashes inside <c>"http://localhost:11434"</c> and turns
/// the rest of the file into garbage.
/// </para>
/// <para>
/// <b>WHAT IT DOES NOT COVER — read this before trusting a green run.</b>
/// </para>
/// <list type="bullet">
/// <item><b>It cannot tell user-visible English from machine-facing English.</b> A tool description
/// sent to the model, an exception message a developer reads, a connector's SQL — this counter sees
/// all of them as prose. That is why the whole-tree test is a RATCHET rather than a zero gate: a
/// number that may not rise is honest about counting things that are fine, whereas a zero gate over
/// this population would be a lie that had to be suppressed within a week. Only the registry files
/// in <see cref="ServiceStringCoverageTests.LocalizedServiceFiles"/> are held at zero.</item>
/// <item><b>One-word labels are invisible.</b> <c>"Failed"</c> written in a service is a real defect
/// this will not find. Requiring two words is what keeps every wire code, enum token, file name and
/// SQL fragment out of the count, and that trade was made deliberately.</item>
/// <item><b>English composed at run time is invisible.</b> <c>prefix + verb + " the file"</c> is
/// three literals of one word each. So is a sentence assembled from constants in another file.</item>
/// <item><b>It only looks under <c>Services/</c>.</b> English in <c>Models/</c>, in <c>Data/</c>, in
/// the MAUI head's C#, or returned by AppManager is outside this scan entirely.</item>
/// <item><b>It says nothing about whether the Hindi is any GOOD.</b> Like every other counter here,
/// it finds English that was never routed through a localizer. A wrong translation is invisible to
/// it.</item>
/// <item><b>The ratchet FREEZES the backlog; it does not fix it.</b> At the baseline this class was
/// written against, 569 prose literals remained across 88 service files, and a large share
/// of them are genuinely user-visible — the connectors surface, the scheduler's
/// <c>CronDescriber</c>, the licensing banners, <c>BackupService</c>'s restore refusals. REQ-UI-051
/// localized the six known sites. Everything else is counted, held, and still English.</item>
/// </list>
/// </remarks>
public static class ServiceStringCoverage
{
    private static readonly Regex InterpolationHole = new(@"\{[^{}]*\}");
    private static readonly Regex LetterRun = new("[A-Za-z]{2,}");

    /// <summary>
    /// Calls whose string arguments are read by a machine rather than by a person.
    /// </summary>
    /// <remarks>
    /// Deliberately SHORT. A logger message template is structured-logging syntax read in a log
    /// viewer; <c>ToString</c> and <c>ParseExact</c> take .NET format strings, which are a contract
    /// with the runtime; <c>nameof</c> is a symbol. Translating any of them breaks the thing rather
    /// than localizing it. Everything else is left IN the count on purpose — this is a ratchet, and
    /// a ratchet that excludes generously is a ratchet that misses the next defect.
    /// </remarks>
    private static readonly Regex MachineFacingCall = new(
        @"[Ll]ogger\s*\.\s*Log|\.\s*ToString\s*\(|ParseExact|nameof\s*\(");

    /// <summary>A literal that is a SQL statement rather than a sentence.</summary>
    /// <remarks>
    /// <c>"DELETE FROM TrAgent WHERE WorkspaceId = @workspaceId"</c> is six words of English by any
    /// word-counting rule and translating it produces a query that does not run. Matched on the
    /// literal itself rather than on the call, because the schema constants in
    /// <c>BackupSchema</c> are plain fields with no call around them.
    /// </remarks>
    private static readonly Regex SqlStatement = new(
        @"\b(SELECT\s|INSERT\s+INTO|DELETE\s+FROM|UPDATE\s+\w|CREATE\s+(TABLE|INDEX|VIRTUAL)|DROP\s+(TABLE|INDEX)|ALTER\s+TABLE|PRAGMA\s)",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Multi-word values that are NOT English UI prose and are excluded from the count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept SEPARATE from <see cref="CodeBlockStringCoverage.MachineText"/> and from
    /// <see cref="RazorStringCoverage.Untranslatable"/> on purpose: those two gate the razor tree
    /// that several clusters edit at once, and a shared list is a shared blast radius.
    /// </para>
    /// <para>
    /// An entry here is a claim that the string is not UI text, and
    /// <c>ServiceStringCoverageTests.TheMachineTextListStaysSmallAndInUse</c> keeps the claim
    /// honest: the list is capped, and an entry no service still contains has to be deleted. The
    /// only entries so far are provider BRAND names, rendered as themselves in Latin script inside
    /// a Devanagari sentence — the same rule the razor counters already apply to them.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlySet<string> MachineText = new HashSet<string>(StringComparer.Ordinal)
    {
        "LM Studio", "Azure AI Foundry", "Google Gemini"
    };

    /// <summary>Locates <c>apps/TechieDesk.Core/Services</c> from the test assembly's location.</summary>
    /// <returns>The absolute services root.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the tree cannot be found.</exception>
    public static string FindServicesRoot()
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
                ", so the service layer could not be located.");
        }

        var services = Path.Combine(directory.FullName, "apps", "TechieDesk.Core", "Services");
        return Directory.Exists(services)
            ? services
            : throw new InvalidOperationException($"'{services}' does not exist.");
    }

    /// <summary>Measures every C# file under <c>Services/</c>.</summary>
    /// <returns>One row per file that carries at least one prose literal, in path order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when implausibly few files were found.</exception>
    public static IReadOnlyList<ServiceFileCoverage> Scan()
    {
        var root = FindServicesRoot();
        var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length < 100)
        {
            throw new InvalidOperationException(
                $"Only {files.Length} .cs files were found under '{root}', so the scan is looking " +
                "in the wrong place and every number below would be a lie.");
        }

        return files
            .Select(path => new ServiceFileCoverage(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                ProseLiterals(File.ReadAllText(path))))
            .Where(row => row.Count > 0)
            .ToArray();
    }

    /// <summary>Measures one file, whether or not it carries anything.</summary>
    /// <param name="source">The file's text.</param>
    /// <param name="relativePath">Its path below <c>Services/</c>.</param>
    /// <returns>The coverage row.</returns>
    public static ServiceFileCoverage Measure(string source, string relativePath) =>
        new(relativePath, ProseLiterals(source));

    /// <summary>Finds the English sentence fragments a file writes as C# literals.</summary>
    /// <param name="code">The file's text.</param>
    /// <returns>The offending literals, in source order.</returns>
    private static IReadOnlyList<string> ProseLiterals(string code)
    {
        var literals = Literals(code).ToArray();
        var masked = Mask(code, literals);
        var found = new List<string>();

        foreach (var (index, value) in literals)
        {
            if (!IsProse(value) || SqlStatement.IsMatch(value) || MachineText.Contains(value))
            {
                continue;
            }

            if (MachineFacingCall.IsMatch(StatementBefore(masked, index)))
            {
                continue;
            }

            found.Add(value);
        }

        return found;
    }

    /// <summary>Lexes the C# string literals out of a file, comments excluded.</summary>
    /// <param name="code">The file's text.</param>
    /// <returns>The index and text of every string literal.</returns>
    /// <remarks>
    /// A single pass rather than a stack of regexes, for the reason
    /// <see cref="CodeBlockStringCoverage"/> records: stripping <c>//</c> comments first eats the
    /// <c>//</c> inside <c>"http://localhost:11434"</c>, unbalances every quote after it and turns
    /// the rest of the file into garbage "literals" the scan then reports. XML doc comments start
    /// with <c>///</c> and are eaten by the same rule, which is what keeps the prose in every
    /// <c>&lt;summary&gt;</c> in this repository out of the count.
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

            // A raw string literal. Its content is skipped rather than measured: the only ones in
            // this tree are JSON tool schemas and SQL, both machine text, and lexing the variable
            // fence length correctly matters more than counting them.
            if (c == '"' && i + 2 < code.Length && code[i + 1] == '"' && code[i + 2] == '"')
            {
                var fence = 0;
                while (i + fence < code.Length && code[i + fence] == '"')
                {
                    fence++;
                }

                var close = code.IndexOf(new string('"', fence), i + fence, StringComparison.Ordinal);
                i = close < 0 ? code.Length : close + fence - 1;
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

    /// <summary>Blanks out the INSIDE of every literal, keeping every index the same.</summary>
    /// <param name="code">The file's text.</param>
    /// <param name="literals">The literals lexed out of it.</param>
    /// <returns>The same text with literal contents replaced by spaces.</returns>
    /// <remarks>
    /// So that <see cref="StatementBefore"/> cannot be stopped by punctuation that is part of a
    /// SENTENCE — a semicolon inside a log message template being the case that broke the razor
    /// counter.
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
    /// <param name="code">The masked file text.</param>
    /// <param name="index">Where the literal starts.</param>
    /// <returns>The text from the last <c>;</c>, <c>{</c> or <c>}</c> up to the literal.</returns>
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
    /// Deliberately the same rule as <see cref="CodeBlockStringCoverage"/>'s, kept as a separate
    /// copy rather than shared: that one gates a registry of razor files and four clusters edit it
    /// at once, and a shared heuristic would mean a tuning change here failing a build over there.
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

    /// <summary>Gets whether a run of words is a CSS utility-class list rather than a sentence.</summary>
    /// <param name="words">The literal's word tokens.</param>
    /// <returns>True when it reads as Tailwind classes.</returns>
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
    /// <returns>The path, the count and the first few offending strings.</returns>
    public static string Describe(ServiceFileCoverage row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var sample = row.Literals.Take(4).Select(value =>
            value.Length <= 70 ? value : value[..70] + "…");

        return $"{row.RelativePath}: {row.Count} — \"{string.Join("\", \"", sample)}\"";
    }
}
