using System.Text;
using System.Text.RegularExpressions;

namespace TechieRag.Connectors.Email;

/// <summary>
/// Removes quoted replies and signatures from a message body (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>Not cosmetic — it is what makes a mailbox searchable.</b> A twelve-message thread that
/// keeps its quoted history contains the first message twelve times, the second eleven times, and so
/// on. Ingested as-is, every chunk of that thread is near-identical to every other chunk, so
/// retrieval returns twelve copies of one answer and the citation points at whichever copy happened
/// to score highest instead of at the message that said it. The same is true of a signature block
/// repeated on every message anyone in the company ever sent.</para>
/// <para><b>It refuses to leave nothing behind.</b> A one-line reply above a long quote is a real
/// message; a message that is <i>only</i> a forward is also a real message, and stripping it to
/// empty would drop content the user asked for. When trimming would remove everything, the original
/// is kept — false negatives cost duplication, false positives cost the document.</para>
/// <para><b>Deliberately English-and-structure-based.</b> The markers below are the structural ones
/// (the RFC 3676 <c>-- </c> delimiter, <c>&gt;</c> quoting, the Outlook and Original-Message
/// separators) plus the common English attribution line. Mail in other languages keeps its quoted
/// history, which is a known limit rather than an oversight — guessing at attribution lines in every
/// language would trim real content.</para>
/// </remarks>
public static partial class ReplyTrimmer
{
    /// <summary>Markers whose appearance at the start of a line ends the message.</summary>
    private static readonly string[] SeparatorMarkers =
    [
        "-----Original Message-----",
        "-----Ursprüngliche Nachricht-----",
        "________________________________",
        "--- Forwarded message ---",
        "---------- Forwarded message ---------",
        "Begin forwarded message:",
    ];

    /// <summary>Trims quoted history and a trailing signature from a message body.</summary>
    /// <param name="body">The decoded message body.</param>
    /// <param name="stripQuotedReplies">Remove quoted history and forwarded blocks.</param>
    /// <param name="stripSignatures">Remove a trailing signature block.</param>
    /// <returns>The trimmed body, or the original when trimming would leave nothing.</returns>
    public static string Trim(string body, bool stripQuotedReplies = true, bool stripSignatures = true)
    {
        if (string.IsNullOrWhiteSpace(body) || (!stripQuotedReplies && !stripSignatures))
        {
            return body ?? string.Empty;
        }

        var lines = body.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            var trimmedLine = line.TrimEnd();

            // RFC 3676: a line of exactly "--" (conventionally with a trailing space) starts the
            // signature, and everything after it is signature by definition.
            if (stripSignatures && (trimmedLine == "--" || line == "-- "))
            {
                break;
            }

            if (!stripQuotedReplies)
            {
                kept.Add(line);
                continue;
            }

            if (IsSeparator(trimmedLine) || AttributionLine().IsMatch(trimmedLine))
            {
                break;
            }

            if (trimmedLine.StartsWith('>'))
            {
                continue;
            }

            kept.Add(line);
        }

        var result = Collapse(kept);

        // Everything was quoted or signature. That is a forward or a bare "+1 " with no text of its
        // own, and the message is worth more whole than empty.
        return string.IsNullOrWhiteSpace(result) ? body : result;
    }

    private static bool IsSeparator(string line)
    {
        foreach (var marker in SeparatorMarkers)
        {
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Collapse(List<string> lines)
    {
        var builder = new StringBuilder();
        var blankRun = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blankRun++;

                // Removing quoted lines leaves the blank lines that separated them, so a trimmed
                // message otherwise ends up mostly whitespace.
                if (blankRun > 1 || builder.Length == 0)
                {
                    continue;
                }

                builder.Append('\n');
                continue;
            }

            blankRun = 0;
            builder.Append(line.TrimEnd()).Append('\n');
        }

        return builder.ToString().Trim();
    }

    /// <summary>Matches the attribution line a client writes above quoted history.</summary>
    /// <returns>A compiled pattern for lines such as "On 3 Jan 2026, Ada Lovelace wrote:".</returns>
    /// <remarks>
    /// Anchored at both ends and required to end in "wrote:" so that an ordinary sentence beginning
    /// with "On" cannot truncate a message. The line is allowed to run long because clients wrap it.
    /// </remarks>
    [GeneratedRegex(@"^\s*(On|Am)\s+.{4,120}\s+(wrote|schrieb)\s*:\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributionLine();
}
