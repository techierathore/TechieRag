using System.Text;
using System.Text.RegularExpressions;

namespace TechieRag.Connectors;

/// <summary>
/// Include/exclude path filtering by glob pattern (REQ-RAG-019 / BRD-63).
/// </summary>
/// <remarks>
/// <para><b>Why a filter is part of the requirement.</b> A repository is mostly not documents.
/// Ingesting one without filters embeds <c>package-lock.json</c>, minified bundles, test fixtures
/// and binary assets, and those dominate the index so thoroughly that real queries retrieve build
/// artefacts. Globs are how a user says "the docs and the source, not the lockfiles" in one line.</para>
/// <para><b>Semantics</b>, chosen to match what people already have in their heads from
/// <c>.gitignore</c> and editor search boxes:</para>
/// <list type="bullet">
/// <item><description><c>*</c> matches within one path segment and never crosses <c>/</c>.</description></item>
/// <item><description><c>**</c> matches any number of segments, including none — <c>docs/**</c> matches <c>docs/a</c> and <c>docs/a/b/c</c>.</description></item>
/// <item><description><c>?</c> matches one character other than <c>/</c>.</description></item>
/// <item><description>A pattern with no <c>/</c> matches the file name alone, so <c>*.md</c> finds <c>docs/deep/readme.md</c>. Without this rule the single most common pattern anyone types would match nothing outside the root.</description></item>
/// <item><description>Matching is case-insensitive. Two of the three hosts are case-sensitive and the third is not, and a user who writes <c>*.MD</c> means <c>*.md</c> on all of them.</description></item>
/// </list>
/// <para><b>Exclude beats include</b>, and an empty include list means "everything". Both are the
/// non-surprising reading of "include these, except those".</para>
/// </remarks>
public sealed class GlobFilter
{
    private readonly Regex[] includes;
    private readonly Regex[] excludes;

    /// <summary>Initializes a new instance of the <see cref="GlobFilter"/> class.</summary>
    /// <param name="includePatterns">Patterns a path must match at least one of. Null or empty includes everything.</param>
    /// <param name="excludePatterns">Patterns that reject a path outright.</param>
    public GlobFilter(IEnumerable<string>? includePatterns, IEnumerable<string>? excludePatterns)
    {
        includes = Compile(includePatterns);
        excludes = Compile(excludePatterns);
    }

    /// <summary>Gets a filter that admits every path.</summary>
    public static GlobFilter AllowAll { get; } = new(null, null);

    /// <summary>Determines whether a path passes the filter.</summary>
    /// <param name="path">A source-relative path using forward slashes, e.g. <c>docs/readme.md</c>.</param>
    /// <returns>True when the path matches an include pattern (or there are none) and no exclude pattern.</returns>
    public bool IsMatch(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/').TrimStart('/');
        var name = normalized[(normalized.LastIndexOf('/') + 1)..];

        foreach (var exclude in excludes)
        {
            if (exclude.IsMatch(normalized) || exclude.IsMatch(name))
            {
                return false;
            }
        }

        if (includes.Length == 0)
        {
            return true;
        }

        foreach (var include in includes)
        {
            if (include.IsMatch(normalized) || include.IsMatch(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Translates one glob pattern into an anchored regular expression.</summary>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns>A case-insensitive regular expression matching whole paths.</returns>
    /// <remarks>Exposed so the translation itself can be asserted, and so callers can reuse the same semantics elsewhere.</remarks>
    public static Regex ToRegex(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        var builder = new StringBuilder("^");
        var normalized = pattern.Replace('\\', '/');

        for (var index = 0; index < normalized.Length; index++)
        {
            var current = normalized[index];
            if (current == '*')
            {
                if (index + 1 < normalized.Length && normalized[index + 1] == '*')
                {
                    index++;

                    // "docs/**" must match "docs" itself as well as "docs/a/b", and "**/x" must
                    // match a bare "x". Folding the following slash into the optional group is what
                    // makes both true; treating ** as plain ".*" would leave a mandatory slash.
                    if (index + 1 < normalized.Length && normalized[index + 1] == '/')
                    {
                        index++;
                        builder.Append("(?:.*/)?");
                    }
                    else
                    {
                        builder.Append(".*");
                    }

                    continue;
                }

                builder.Append("[^/]*");
                continue;
            }

            if (current == '?')
            {
                builder.Append("[^/]");
                continue;
            }

            builder.Append(Regex.Escape(current.ToString()));
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static Regex[] Compile(IEnumerable<string>? patterns) =>
        patterns is null
            ? []
            : [.. patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => ToRegex(p.Trim()))];
}
