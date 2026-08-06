using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TechieRag.Web;

namespace TechieRag.Connectors.Email;

/// <summary>
/// Turns a raw RFC 5322 message into text and attachments (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>Why this is written rather than taken from a package.</b> The library's rule is raw
/// <see cref="System.Net.Http.HttpClient"/> and <c>System.Text.Json</c> for provider code, and .NET
/// ships no MIME reader — <see cref="System.Net.Mail"/> only writes. Everything below is the subset
/// of MIME that real mail actually uses: unfolded headers, encoded words, nested multiparts, base64
/// and quoted-printable, and a declared charset. It is not a general MIME implementation, and where
/// it meets something outside that subset it degrades to legible text rather than throwing.</para>
/// <para><b>Bytes are carried through Latin-1, on purpose.</b> Latin-1 maps bytes 0–255 onto chars
/// 0–255 and back without loss, so the structural work — finding boundaries, splitting headers from
/// body — can be done on a string while the payload bytes stay exactly as they arrived. Decoding to
/// UTF-8 first would corrupt every part whose charset is something else, before its charset has even
/// been read.</para>
/// <para><b>Plain text is preferred over HTML.</b> Both are usually present and say the same thing;
/// the plain part is what the sender's client generated from the same source and does not need
/// stripping. HTML is used only when it is all there is.</para>
/// </remarks>
public static partial class MimeParser
{
    /// <summary>The most attachments one message will yield.</summary>
    /// <remarks>
    /// <para>A part costs far more decoded than it does on the wire: a 40 MB message built from
    /// hundreds of thousands of one-byte parts decodes to roughly ten times its own size in live
    /// objects, all of it retained because <see cref="ParsedMailMessage.Attachments"/> holds it.
    /// That is a message anyone can send, so the count is bounded here rather than hoped about.</para>
    /// <para>The bound is far above any message a person composes. Where it bites, the message's
    /// text and its first <see cref="MaxAttachments"/> files still parse — the connector reports the
    /// truncation rather than the message being lost.</para>
    /// </remarks>
    public const int MaxAttachments = 1000;

    private const string DefaultCharset = "utf-8";

    /// <summary>Parses a raw message.</summary>
    /// <param name="raw">The message exactly as the server holds it, headers and body.</param>
    /// <returns>The decoded message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="raw"/> is null.</exception>
    /// <remarks>Never throws on malformed input. A message this cannot make sense of yields empty text, which the connector reports as a per-message skip.</remarks>
    public static ParsedMailMessage Parse(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var content = Encoding.Latin1.GetString(raw);
        var (headerBlock, body) = SplitHeaders(content);
        var headers = ParseHeaders(headerBlock);

        var texts = new List<string>();
        var htmls = new List<string>();
        var attachments = new List<MailAttachment>();
        ReadPart(headers, body, texts, htmls, attachments, depth: 0);

        var text = texts.Count > 0
            ? string.Join("\n\n", texts)
            : htmls.Count > 0
                ? WebPageReader.Read(string.Join("\n", htmls), "mail://message").Text
                : string.Empty;

        return new ParsedMailMessage(
            headers,
            DecodeEncodedWords(Header(headers, "Subject")),
            DecodeEncodedWords(Header(headers, "From")),
            DecodeEncodedWords(Header(headers, "To")),
            ParseDate(Header(headers, "Date")),
            NullIfEmpty(Header(headers, "Message-ID")),
            text.Trim(),
            attachments);
    }

    /// <summary>Parses a header block on its own.</summary>
    /// <param name="headerBlock">Header lines, without the blank line that ends them.</param>
    /// <returns>Unfolded headers keyed case-insensitively. Repeated headers keep the first value.</returns>
    /// <remarks>
    /// Exposed because IMAP can return headers without a body, which is exactly what makes filtering
    /// possible without downloading messages. Folded headers — a long subject continued on an
    /// indented line — are rejoined here; treating the continuation as its own header is the classic
    /// way a long subject line comes out truncated.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> ParseHeaders(string headerBlock)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(headerBlock))
        {
            return headers;
        }

        var name = (string?)null;
        var value = new StringBuilder();

        foreach (var line in headerBlock.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && name is not null)
            {
                value.Append(' ').Append(line.Trim());
                continue;
            }

            Commit(headers, name, value);
            name = null;

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            name = line[..colon].Trim();
            value.Append(line[(colon + 1)..].Trim());
        }

        Commit(headers, name, value);
        return headers;
    }

    /// <summary>Decodes RFC 2047 encoded words in a header value.</summary>
    /// <param name="value">A header value that may contain <c>=?charset?B?...?=</c> or <c>=?charset?Q?...?=</c> runs.</param>
    /// <returns>The value with every encoded word decoded, and unencoded runs untouched.</returns>
    /// <remarks>
    /// Any subject that is not pure ASCII arrives encoded this way. Without this step a message from
    /// a colleague whose name carries an accent is indexed under a subject reading
    /// <c>=?UTF-8?B?...?=</c>, which is both unreadable and unsearchable.
    /// </remarks>
    public static string DecodeEncodedWords(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("=?", StringComparison.Ordinal))
        {
            return value;
        }

        return EncodedWordPattern().Replace(value, match =>
        {
            var charset = match.Groups[1].Value;
            var kind = match.Groups[2].Value.ToUpperInvariant();
            var payload = match.Groups[3].Value;

            try
            {
                var bytes = kind == "B"
                    ? Convert.FromBase64String(payload)
                    // In an encoded word '_' means space; that is the one place quoted-printable
                    // differs from its use in a body, and missing it runs every word together.
                    : DecodeQuotedPrintable(payload.Replace('_', ' '));

                return ResolveEncoding(charset).GetString(bytes);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or DecoderFallbackException)
            {
                // A malformed encoded word is worth leaving as-is: the surrounding text is still
                // useful, and refusing the whole header would lose a subject over one bad run.
                return match.Value;
            }
        });
    }

    /// <summary>Decodes quoted-printable content.</summary>
    /// <param name="value">The encoded text.</param>
    /// <returns>The decoded bytes.</returns>
    public static byte[] DecodeQuotedPrintable(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var bytes = new List<byte>(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current != '=')
            {
                bytes.Add((byte)current);
                continue;
            }

            // A trailing '=' is a soft line break: the encoder split a long line and the newline is
            // not part of the content. Emitting it would put a break in the middle of a word.
            if (index + 2 < value.Length && value[index + 1] == '\r' && value[index + 2] == '\n')
            {
                index += 2;
                continue;
            }

            if (index + 1 < value.Length && value[index + 1] == '\n')
            {
                index += 1;
                continue;
            }

            if (index + 2 < value.Length
                && byte.TryParse(
                    value.Substring(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var decoded))
            {
                bytes.Add(decoded);
                index += 2;
                continue;
            }

            bytes.Add((byte)current);
        }

        return [.. bytes];
    }

    /// <summary>Reads one parameter out of a structured header value.</summary>
    /// <param name="headerValue">A value such as <c>text/plain; charset="utf-8"</c>.</param>
    /// <param name="parameter">The parameter name, matched case-insensitively.</param>
    /// <returns>The parameter's value with any quotes removed, or null when absent.</returns>
    public static string? ReadParameter(string headerValue, string parameter)
    {
        if (string.IsNullOrEmpty(headerValue) || string.IsNullOrEmpty(parameter))
        {
            return null;
        }

        foreach (var segment in headerValue.Split(';'))
        {
            var equals = segment.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            // RFC 2231 splits long parameters into name*0, name*1 … and marks charset-tagged ones
            // with a trailing '*'. Only the trailing '*' is handled; a split filename yields its
            // first segment, which is a truncated name rather than a wrong one.
            var key = segment[..equals].Trim().TrimEnd('*');
            if (!string.Equals(key, parameter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return segment[(equals + 1)..].Trim().Trim('"').Trim();
        }

        return null;
    }

    private static void ReadPart(
        IReadOnlyDictionary<string, string> headers,
        string body,
        List<string> texts,
        List<string> htmls,
        List<MailAttachment> attachments,
        int depth)
    {
        // Nesting is real (a signed message wrapping a mixed message wrapping an alternative), but
        // it is shallow. A depth cap stops a malformed or hostile message from recursing forever.
        if (depth > 10)
        {
            return;
        }

        var contentType = Header(headers, "Content-Type");
        var mediaType = MediaTypeOf(contentType);

        if (mediaType.StartsWith("multipart/", StringComparison.Ordinal))
        {
            var boundary = ReadParameter(contentType, "boundary");
            if (string.IsNullOrEmpty(boundary))
            {
                return;
            }

            foreach (var section in SplitMultipart(body, boundary))
            {
                var (partHeaderBlock, partBody) = SplitHeaders(section);
                ReadPart(ParseHeaders(partHeaderBlock), partBody, texts, htmls, attachments, depth + 1);
            }

            return;
        }

        var disposition = Header(headers, "Content-Disposition");
        var fileName = ReadParameter(disposition, "filename") ?? ReadParameter(contentType, "name");
        var content = DecodeTransfer(body, Header(headers, "Content-Transfer-Encoding"));

        var isAttachment = disposition.StartsWith("attachment", StringComparison.OrdinalIgnoreCase)
                           || (fileName is not null && !mediaType.StartsWith("text/", StringComparison.Ordinal));

        if (isAttachment)
        {
            if (attachments.Count >= MaxAttachments)
            {
                return;
            }

            attachments.Add(new MailAttachment(
                SafeFileName(DecodeEncodedWords(fileName ?? "attachment")),
                mediaType.Length == 0 ? "application/octet-stream" : mediaType,
                content));
            return;
        }

        var charset = ReadParameter(contentType, "charset") ?? DefaultCharset;
        var decoded = ResolveEncoding(charset).GetString(content);

        if (mediaType == "text/html")
        {
            htmls.Add(decoded);
            return;
        }

        // An unlabelled part is text: a bare message with no Content-Type at all is the oldest and
        // still most common shape of a plain mail, and dropping it would lose the whole body.
        if (mediaType.Length == 0 || mediaType.StartsWith("text/", StringComparison.Ordinal))
        {
            texts.Add(decoded);
        }
    }

    /// <summary>Splits a multipart body on its boundary.</summary>
    /// <param name="body">The multipart body.</param>
    /// <param name="boundary">The boundary declared in the Content-Type header.</param>
    /// <returns>Each part's raw text, preamble and epilogue discarded.</returns>
    private static IEnumerable<string> SplitMultipart(string body, string boundary)
    {
        var delimiter = "--" + boundary;
        var parts = body.Split(delimiter);

        // parts[0] is the preamble, which is prose for clients that cannot read MIME and is never
        // content. The final part starts with "--", the closing delimiter, and is the epilogue.
        for (var index = 1; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.StartsWith("--", StringComparison.Ordinal))
            {
                yield break;
            }

            yield return part.TrimStart('\r', '\n');
        }
    }

    private static (string Headers, string Body) SplitHeaders(string content)
    {
        var index = content.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (index >= 0)
        {
            return (content[..index], content[(index + 4)..]);
        }

        index = content.IndexOf("\n\n", StringComparison.Ordinal);
        return index >= 0
            ? (content[..index], content[(index + 2)..])
            : (content, string.Empty);
    }

    private static byte[] DecodeTransfer(string body, string encoding)
    {
        var normalized = encoding.Trim().ToLowerInvariant();

        if (normalized == "base64")
        {
            try
            {
                return Convert.FromBase64String(Base64Noise().Replace(body, string.Empty));
            }
            catch (FormatException)
            {
                // Truncated base64 is common in mail that passed through a broken relay. Falling
                // back to the raw bytes yields gibberish for that one part instead of losing the
                // whole message.
                return Encoding.Latin1.GetBytes(body);
            }
        }

        return normalized == "quoted-printable"
            ? DecodeQuotedPrintable(body)
            : Encoding.Latin1.GetBytes(body);
    }

    /// <summary>Resolves a declared charset to an encoding.</summary>
    /// <param name="charset">The charset name from a Content-Type header.</param>
    /// <returns>The matching encoding, or UTF-8 when the name is unknown to this runtime.</returns>
    /// <remarks>
    /// Unknown charsets fall back rather than fail. Mail carries charset labels that no longer
    /// resolve anywhere, and a message that arrives slightly mis-decoded is worth more than a
    /// message that does not arrive.
    /// </remarks>
    private static Encoding ResolveEncoding(string charset)
    {
        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static string MediaTypeOf(string contentType)
    {
        var semicolon = contentType.IndexOf(';');
        return (semicolon < 0 ? contentType : contentType[..semicolon]).Trim().ToLowerInvariant();
    }

    /// <summary>Reduces an attachment name to a bare file name.</summary>
    /// <param name="fileName">The name as the message declared it.</param>
    /// <returns>A name with no directory component and no traversal.</returns>
    /// <remarks>
    /// <para>A sender controls this string completely, and a name like
    /// <c>../../.ssh/authorized_keys</c> is a real thing that arrives in real mail. Nothing here
    /// writes attachments to disk, but this value is handed to a document processor as the document
    /// name and shown to a user, so it is reduced to a bare name at the point it is parsed rather
    /// than trusted downstream.</para>
    /// <para>The colon goes too. <c>report.pdf:evil.exe</c> names an NTFS alternate data stream, and
    /// on Windows a downstream component that does open the name by path opens the stream rather
    /// than the file — a difference invisible in every listing that shows the name.</para>
    /// </remarks>
    private static string SafeFileName(string fileName)
    {
        var trimmed = fileName.Replace('\\', '/');
        var slash = trimmed.LastIndexOf('/');
        var name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;

        var cleaned = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            cleaned.Append(character is ':' or '/' || char.IsControl(character) ? '_' : character);
        }

        var result = cleaned.ToString().Trim().TrimStart('.');
        return string.IsNullOrWhiteSpace(result) ? "attachment" : result;
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Mail dates often carry a trailing comment such as "(GMT)", which no parser accepts.
        var cleaned = DateComment().Replace(value, string.Empty).Trim();

        return DateTimeOffset.TryParse(
            cleaned, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }

    private static void Commit(Dictionary<string, string> headers, string? name, StringBuilder value)
    {
        if (name is not null && !headers.ContainsKey(name))
        {
            headers[name] = value.ToString();
        }

        value.Clear();
    }

    private static string Header(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var value) ? value : string.Empty;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex(@"=\?([^?]+)\?([BbQq])\?([^?]*)\?=", RegexOptions.CultureInvariant)]
    private static partial Regex EncodedWordPattern();

    [GeneratedRegex(@"[^A-Za-z0-9+/=]", RegexOptions.CultureInvariant)]
    private static partial Regex Base64Noise();

    [GeneratedRegex(@"\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex DateComment();
}
