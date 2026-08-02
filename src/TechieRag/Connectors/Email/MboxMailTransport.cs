using System.Text;

namespace TechieRag.Connectors.Email;

/// <summary>
/// Reads a local <c>.mbox</c> file as a mailbox (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>Why a file transport exists at all.</b> BRD-135 names a local mbox alongside the three
/// IMAP providers, and it is the shape most mail exports take — every "download your data" flow
/// from the major providers produces one. It is also the only way to ingest an account that no
/// longer exists.</para>
/// <para><b>It is the honest second implementation of the seam.</b> Two real implementations of
/// <see cref="IMailTransport"/> is what keeps that interface from being an abstraction over exactly
/// one thing, and this one needs no network, no server and no credentials to exercise
/// end-to-end.</para>
/// <para><b>Message-ID is preferred as the identifier.</b> Position in the file would make every
/// message's identity depend on how many messages precede it, so appending to an archive would
/// re-ingest all of it. Falling back to position happens only for messages with no Message-ID.</para>
/// <para><b>The file is read into memory once.</b> Splitting an mbox requires seeing message
/// boundaries, and a mail archive is text; a very large export will cost its own size in RAM. That
/// is a stated limit, not a hidden one.</para>
/// </remarks>
public sealed class MboxMailTransport : IMailTransport
{
    private readonly string path;
    private readonly List<Entry> entries = [];
    private readonly Dictionary<string, Entry> byUid = new(StringComparer.Ordinal);
    private bool loaded;

    /// <summary>Initializes a new instance of the <see cref="MboxMailTransport"/> class.</summary>
    /// <param name="path">Path to the <c>.mbox</c> file.</param>
    public MboxMailTransport(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        this.path = path;
    }

    /// <inheritdoc />
    public string MailboxName => Path.GetFileName(path);

    /// <summary>Gets the single folder name an mbox presents.</summary>
    /// <remarks>An mbox is one flat file with no folder structure, so it reports itself as one folder named after the file.</remarks>
    public string FolderName => Path.GetFileNameWithoutExtension(path);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([FolderName]);

    /// <inheritdoc />
    public Task<MailSearchPage> SearchAsync(
        string folder,
        MailSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        Load();

        // Every criterion is applied here because there is no server to push it to. Ignoring one
        // because the transport cannot express it would make the same filter mean different things
        // on different sources, which is worse than applying it slowly.
        var matches = entries.Where(entry => Matches(entry, criteria)).ToList();
        var slice = matches.Skip(Math.Max(0, skip)).Take(Math.Max(0, take)).Select(e => e.Header).ToList();

        return Task.FromResult(new MailSearchPage(slice, skip + slice.Count < matches.Count));
    }

    /// <inheritdoc />
    public Task<byte[]> FetchAsync(MailHeader header, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(header);
        Load();

        return byUid.TryGetValue(header.Uid, out var entry)
            ? Task.FromResult(entry.Raw)
            : throw new InvalidOperationException($"'{header.Subject}' is no longer present in {MailboxName}.");
    }

    private static bool Matches(Entry entry, MailSearchCriteria criteria)
    {
        if (criteria.SinceUtc is { } since && entry.Header.Date is { } date && date < since.Date)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(criteria.SenderContains)
            && !entry.Header.From.Contains(criteria.SenderContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(criteria.SubjectContains)
               || entry.Header.Subject.Contains(criteria.SubjectContains, StringComparison.OrdinalIgnoreCase);
    }

    private void Load()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;

        if (!File.Exists(path))
        {
            throw new ConnectorException("email", $"'{path}' does not exist.");
        }

        var content = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        var index = 0;

        foreach (var raw in Split(content))
        {
            var message = MimeParser.Parse(Encoding.Latin1.GetBytes(raw));

            // Position is the fallback, not the identity: an archive that gains messages at the
            // front would otherwise renumber every message in it and re-ingest the whole file.
            var uid = message.MessageId ?? $"#{index}";
            var header = new MailHeader(
                FolderName,
                uid,
                "mbox",
                message.Subject,
                message.From,
                message.To,
                message.Date,
                raw.Length,
                message.MessageId);

            var entry = new Entry(header, Encoding.Latin1.GetBytes(raw));
            entries.Add(entry);
            byUid[uid] = entry;
            index++;
        }
    }

    /// <summary>Splits an mbox file into individual messages.</summary>
    /// <param name="content">The whole file, read byte-preservingly.</param>
    /// <returns>Each message's raw text.</returns>
    /// <remarks>
    /// <para>The format has no length prefix: a message ends where the next line beginning
    /// <c>From </c> begins. Because that line can also occur inside a body, writers escape it as
    /// <c>&gt;From </c>, and unescaping it here is what stops a quoted "From " at the start of a
    /// line from silently truncating the message that contains it.</para>
    /// <para>The escape is applied to a line that <i>already</i> began with <c>&gt;</c>, so a quoted
    /// reply containing <c>&gt;From the desk of…</c> is written out as <c>&gt;&gt;From …</c>. One
    /// <c>&gt;</c> is removed from any run of them, per mboxrd — stripping only the single-<c>&gt;</c>
    /// case leaves every deeper quote one level too deep, which is a body the sender did not write.</para>
    /// </remarks>
    private static IEnumerable<string> Split(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith("From ", StringComparison.Ordinal))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                // The separator line itself is not part of the message; it carries only the
                // envelope sender and a timestamp that the headers state more accurately.
                continue;
            }

            current.Append(IsEscapedFromLine(line) ? line[1..] : line).Append('\n');
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    /// <summary>Determines whether a line is a "From " line the writer escaped.</summary>
    /// <param name="line">A body line.</param>
    /// <returns>True when the line is one or more <c>&gt;</c> followed by <c>From </c>.</returns>
    private static bool IsEscapedFromLine(string line)
    {
        var index = 0;
        while (index < line.Length && line[index] == '>')
        {
            index++;
        }

        return index > 0 && line.AsSpan(index).StartsWith("From ", StringComparison.Ordinal);
    }

    private sealed record Entry(MailHeader Header, byte[] Raw);
}
