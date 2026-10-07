using System.Text;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors.Email;

/// <summary>
/// Content the MIME parser leaves out is reported on <see cref="ParsedMailMessage.Notes"/>, never
/// dropped without notice (REQ-RAG-119 / BRD-180, Sevak feedback TR-RAG-035).
/// </summary>
public sealed class MailParseNotesTests
{
    /// <summary>
    /// A developer parses a message whose text part sits one level past the depth limit: the parsed
    /// message carries a note naming that part, with the nesting code and depth.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-119 NestedPastDepthLimitIsNoted")]
    public void NestedPastDepthLimitIsNoted()
    {
        var raw = "Subject: nested\r\n" + Nest(0, MimeParser.MaxNestingDepth + 1);

        var parsed = MimeParser.Parse(Encoding.Latin1.GetBytes(raw));

        var note = Assert.Single(parsed.Notes);
        Assert.Equal(MailParseCodes.NestingTooDeep, note.Code);
        Assert.Equal("deep-note.txt", note.PartName);
        Assert.Equal(MimeParser.MaxNestingDepth + 1, note.Depth);
        Assert.DoesNotContain("hidden text", parsed.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same part one level shallower is decoded, and the notes list is empty — the list is an
    /// addition that existing callers never see unless something was left out.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-119 FullyReadMessageHasNoNotes")]
    public void FullyReadMessageHasNoNotes()
    {
        var raw = "Subject: nested\r\n" + Nest(0, MimeParser.MaxNestingDepth);

        var parsed = MimeParser.Parse(Encoding.Latin1.GetBytes(raw));

        Assert.Empty(parsed.Notes);
        Assert.Contains("hidden text", parsed.Body, StringComparison.Ordinal);
    }

    /// <summary>Builds multiparts nested down to a text part at <paramref name="leafDepth"/>.</summary>
    private static string Nest(int level, int leafDepth)
    {
        if (level == leafDepth)
        {
            return "Content-Type: text/plain; charset=utf-8; name=\"deep-note.txt\"\r\n"
                + "Content-Disposition: inline; filename=\"deep-note.txt\"\r\n\r\nhidden text\r\n";
        }

        var boundary = $"bound-{level:D2}-end";
        return $"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n--{boundary}\r\n"
            + Nest(level + 1, leafDepth)
            + $"\r\n--{boundary}--\r\n";
    }
}
