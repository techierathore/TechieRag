using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Connectors.Email;

/// <summary>
/// Reads a mailbox over IMAP (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>Why hand-written.</b> The library's rule is raw sockets and <c>System.Text.Json</c> for
/// provider code, and taking a MIME/IMAP package would add a large dependency — and its transitive
/// tree — to a package whose whole pitch is being light. What is implemented here is the subset the
/// requirement names: authenticate, list folders, search with the scope filters, fetch headers,
/// fetch a message. It is not a general IMAP client, and it does not pretend to be one: unsupported
/// server behaviour surfaces as an operator-facing failure, not as silently missing mail.</para>
/// <para><b>Read-only by construction.</b> Every fetch uses <c>BODY.PEEK</c> rather than
/// <c>BODY</c>, so ingesting a mailbox does not mark it as read. A connector that silently marked a
/// year of mail as read would be an unrecoverable act on someone's inbox. Beyond that, the session
/// this type talks through is created read-only: it refuses, before anything reaches the wire, every
/// command outside <c>CAPABILITY</c>, <c>LOGIN</c>, <c>AUTHENTICATE</c>, <c>LIST</c>, <c>SELECT</c>,
/// <c>EXAMINE</c>, <c>SEARCH</c>, <c>FETCH</c> and their read-only kin, and any non-<c>PEEK</c> body
/// fetch. Acting on mail (move, label, Trash) is a separate type, <see cref="ImapMailActions"/>
/// (REQ-RAG-110 / BRD-169), so ingesting a mailbox can never change it.</para>
/// <para><b>Searching happens on the server.</b> Date, sender and subject are IMAP <c>SEARCH</c>
/// keys, so the scope filters are evaluated before anything is transferred. Only the UIDs of
/// matching messages come back, and headers are fetched a page at a time from that list.</para>
/// <para><b>Nothing is logged that could identify a credential.</b> Command text is never logged,
/// because the <c>LOGIN</c> and <c>AUTHENTICATE</c> commands are command text.</para>
/// <para><b>Nothing a caller supplies reaches the wire unchecked.</b> IMAP is a line protocol, so a
/// folder name, filter or account name carrying a carriage return is not a badly-formed argument —
/// it is a second command, executed with this account's credentials. Every such value is refused
/// before it is sent, and the shared session refuses a composed command line containing a line
/// break as a last resort, so a future call site that forgets cannot reintroduce the hole.</para>
/// <para><b>Every response is bounded.</b> The server declares how many bytes a literal will carry
/// and how many untagged lines it will send, and both are numbers the server chooses. Neither is
/// honoured beyond a stated bound.</para>
/// </remarks>
public sealed partial class ImapMailTransport : IMailTransport, IDisposable
{
    /// <summary>The most untagged response lines one command may produce before the server is judged hostile.</summary>
    /// <remarks>
    /// A command ends when its tagged completion arrives. A server that never sends one, and keeps
    /// sending untagged lines instead, is answered by giving up: without this bound the reply is
    /// accumulated until the process runs out of memory. No real LIST, SEARCH or FETCH response is
    /// within two orders of magnitude of this.
    /// </remarks>
    public const int MaxResponseLines = ImapSession.MaxResponseLines;

    private readonly ImapMailboxOptions options;
    private readonly Dictionary<string, List<string>> searchCache = new(StringComparer.Ordinal);
    private readonly ImapSession session;
    private string? selectedFolder;
    private string uidValidity = "0";

    /// <summary>Initializes a new instance of the <see cref="ImapMailTransport"/> class.</summary>
    /// <param name="connectionFactory">Creates the byte pipe to the server. Tests pass a scripted fake; production passes <see cref="SocketImapConnection"/>.</param>
    /// <param name="options">Server address and credentials.</param>
    /// <param name="logger">Diagnostics. Never receives command text.</param>
    public ImapMailTransport(
        Func<IImapConnection> connectionFactory,
        ImapMailboxOptions options,
        ILogger<ImapMailTransport>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        this.options = options ?? throw new ArgumentNullException(nameof(options));

        // Read-only by construction: this session refuses every verb outside the non-mutating
        // allowlist, so no code path in this type can change the mailbox it reads.
        session = new ImapSession(
            connectionFactory, options, logger ?? NullLogger<ImapMailTransport>.Instance, readOnly: true);
    }

    /// <summary>Creates a transport that connects to a real server over TLS.</summary>
    /// <param name="options">Server address and credentials.</param>
    /// <param name="logger">Diagnostics.</param>
    /// <returns>A transport the caller owns and disposes.</returns>
    /// <exception cref="ArgumentException"><see cref="ImapMailboxOptions.Host"/> is empty.</exception>
    public static ImapMailTransport Create(ImapMailboxOptions options, ILogger<ImapMailTransport>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new ArgumentException("An IMAP transport needs a Host.", nameof(options));
        }

        return new ImapMailTransport(
            () => new SocketImapConnection(options.Host, options.Port, options.Timeout), options, logger);
    }

    /// <inheritdoc />
    public string MailboxName => options.Host;

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken cancellationToken = default)
    {
        await session.EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var result = await session.RunAsync("LIST \"\" \"*\"", cancellationToken).ConfigureAwait(false);
        session.Ensure(result, "list folders");

        var folders = new List<string>();
        foreach (var line in result.Lines)
        {
            var name = ParseListLine(line.Text);
            if (name is not null)
            {
                folders.Add(name);
            }
        }

        return folders;
    }

    /// <inheritdoc />
    public async Task<MailSearchPage> SearchAsync(
        string folder,
        MailSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(criteria);

        await SelectAsync(folder, cancellationToken).ConfigureAwait(false);
        var uids = await ResolveUidsAsync(folder, criteria, cancellationToken).ConfigureAwait(false);

        var slice = uids.Skip(Math.Max(0, skip)).Take(Math.Max(0, take)).ToList();
        if (slice.Count == 0)
        {
            return new MailSearchPage([], false);
        }

        // BODY.PEEK[HEADER] rather than the ENVELOPE structure: the header block goes straight to
        // MimeParser, which already decodes encoded words and unfolds continuations, instead of
        // needing a second parser for IMAP's own parenthesised envelope grammar.
        var command = $"UID FETCH {string.Join(",", slice)} (UID INTERNALDATE RFC822.SIZE BODY.PEEK[HEADER])";
        var result = await session.RunAsync(command, cancellationToken).ConfigureAwait(false);
        session.Ensure(result, $"read headers from '{folder}'");

        var headers = new List<MailHeader>(slice.Count);
        foreach (var line in result.Lines)
        {
            var header = ParseHeaderLine(folder, line);
            if (header is not null)
            {
                headers.Add(header);
            }
        }

        return new MailSearchPage(headers, skip + slice.Count < uids.Count);
    }

    /// <inheritdoc />
    public async Task<byte[]> FetchAsync(MailHeader header, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(header);

        var uid = RequireUid(header.Uid);

        await SelectAsync(header.Folder, cancellationToken).ConfigureAwait(false);
        var result = await session.RunAsync($"UID FETCH {uid} (BODY.PEEK[])", cancellationToken).ConfigureAwait(false);
        session.Ensure(result, $"read message {header.Uid}");

        foreach (var line in result.Lines)
        {
            if (line.Literals.Count > 0)
            {
                return line.Literals[0];
            }
        }

        // The server accepted the command and returned no body. That is a message that vanished
        // between the search and the fetch, which costs this message and not the run.
        throw new InvalidOperationException(
            $"Message {header.Uid} in '{header.Folder}' returned no content; it may have been deleted.");
    }

    /// <inheritdoc />
    public void Dispose() => session.Dispose();

    private async Task SelectAsync(string folder, CancellationToken cancellationToken)
    {
        await session.EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        if (string.Equals(selectedFolder, folder, StringComparison.Ordinal))
        {
            return;
        }

        var result = await session.RunAsync($"SELECT {Quote(folder, "folder name")}", cancellationToken).ConfigureAwait(false);
        if (!result.IsOk)
        {
            throw new ConnectorException(
                "email", $"Folder '{folder}' could not be opened on {options.Host}. Check the name and its hierarchy separator.");
        }

        selectedFolder = folder;
        uidValidity = "0";

        foreach (var line in result.Lines)
        {
            var match = UidValidityPattern().Match(line.Text);
            if (match.Success)
            {
                uidValidity = match.Groups[1].Value;
                break;
            }
        }
    }

    private async Task<List<string>> ResolveUidsAsync(
        string folder,
        MailSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        var key = $"{folder}|{uidValidity}|{BuildSearchKeys(criteria)}";
        if (searchCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = await session.RunAsync($"UID SEARCH {BuildSearchKeys(criteria)}", cancellationToken).ConfigureAwait(false);
        session.Ensure(result, $"search '{folder}'");

        var uids = new List<long>();
        foreach (var line in result.Lines)
        {
            var match = SearchResultPattern().Match(line.Text);
            if (!match.Success)
            {
                continue;
            }

            foreach (var token in match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid))
                {
                    uids.Add(uid);
                }
            }
        }

        // Sorted ascending so paging is stable and the oldest mail is ingested first. Servers are
        // not required to return SEARCH results in any order.
        uids.Sort();
        var ordered = uids.Select(u => u.ToString(CultureInfo.InvariantCulture)).ToList();
        searchCache[key] = ordered;
        return ordered;
    }

    /// <summary>Builds the IMAP SEARCH keys for a set of criteria.</summary>
    /// <param name="criteria">The scope filters.</param>
    /// <returns>A SEARCH key string, never empty.</returns>
    /// <remarks>
    /// Keys separated by spaces are an implicit AND in IMAP. <c>ALL</c> is emitted when there is
    /// nothing to filter on, because a bare <c>UID SEARCH</c> with no key is a syntax error rather
    /// than a match-everything.
    /// </remarks>
    internal static string BuildSearchKeys(MailSearchCriteria criteria)
    {
        var keys = new List<string>();

        if (criteria.SinceUtc is { } since)
        {
            // IMAP dates are day-granular and must be in this exact form.
            keys.Add(string.Create(CultureInfo.InvariantCulture, $"SINCE {since.UtcDateTime:dd-MMM-yyyy}"));
        }

        if (!string.IsNullOrWhiteSpace(criteria.SenderContains))
        {
            keys.Add($"FROM {Quote(criteria.SenderContains, "sender filter")}");
        }

        if (!string.IsNullOrWhiteSpace(criteria.SubjectContains))
        {
            keys.Add($"SUBJECT {Quote(criteria.SubjectContains, "subject filter")}");
        }

        return keys.Count == 0 ? "ALL" : string.Join(" ", keys);
    }

    /// <summary>Reads a folder name out of an untagged LIST response.</summary>
    /// <param name="line">The response line.</param>
    /// <returns>The folder name, or null when the line is not a selectable folder.</returns>
    /// <remarks>
    /// The name is the last token and may be quoted, because folder names contain spaces.
    /// <c>\Noselect</c> folders are container nodes that hold no mail and cannot be opened; returning
    /// them would produce a failure for every one of them on any server that uses a hierarchy.
    /// </remarks>
    internal static string? ParseListLine(string line)
    {
        if (!line.StartsWith("* LIST", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (line.Contains("\\Noselect", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var close = line.IndexOf(')');
        var remainder = close >= 0 ? line[(close + 1)..].Trim() : line["* LIST".Length..].Trim();

        var tokens = Tokenize(remainder);
        return tokens.Count == 0 ? null : tokens[^1];
    }

    private MailHeader? ParseHeaderLine(string folder, ImapLine line)
    {
        if (!line.Text.Contains("FETCH", StringComparison.OrdinalIgnoreCase) || line.Literals.Count == 0)
        {
            return null;
        }

        var uid = UidPattern().Match(line.Text);
        if (!uid.Success)
        {
            return null;
        }

        var headers = MimeParser.ParseHeaders(Encoding.Latin1.GetString(line.Literals[0]));

        long? size = null;
        var sizeMatch = SizePattern().Match(line.Text);
        if (sizeMatch.Success && long.TryParse(sizeMatch.Groups[1].Value, out var parsedSize))
        {
            size = parsedSize;
        }

        // The Date header is the sender's clock and is routinely wrong or absent; INTERNALDATE is
        // when this server received the message, which is what a date filter should mean.
        var date = ParseInternalDate(line.Text)
                   ?? (headers.TryGetValue("Date", out var raw) ? ParseHeaderDate(raw) : null);

        return new MailHeader(
            folder,
            uid.Groups[1].Value,
            uidValidity,
            MimeParser.DecodeEncodedWords(Read(headers, "Subject")),
            MimeParser.DecodeEncodedWords(Read(headers, "From")),
            MimeParser.DecodeEncodedWords(Read(headers, "To")),
            date,
            size,
            headers.TryGetValue("Message-ID", out var id) ? id : null);
    }

    /// <summary>Reads the byte count of a trailing IMAP literal.</summary>
    /// <param name="line">A response line.</param>
    /// <param name="length">The literal's length when one is present.</param>
    /// <returns>True when the line ends in a literal marker.</returns>
    internal static bool TryReadLiteralLength(string line, out int length) =>
        ImapSession.TryReadLiteralLength(line, out length);

    /// <summary>Splits an IMAP response fragment into quoted strings and bare atoms.</summary>
    /// <param name="value">The fragment.</param>
    /// <returns>The tokens, with quotes removed and escapes resolved.</returns>
    internal static List<string> Tokenize(string value) => ImapSession.Tokenize(value);

    private static DateTimeOffset? ParseInternalDate(string line)
    {
        var match = InternalDatePattern().Match(line);
        if (!match.Success)
        {
            return null;
        }

        string[] formats = ["dd-MMM-yyyy HH:mm:ss zzz", "d-MMM-yyyy HH:mm:ss zzz"];
        return DateTimeOffset.TryParseExact(
            match.Groups[1].Value.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ParseHeaderDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;

    private static string Read(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var value) ? value : string.Empty;

    private static string Quote(string value, string what) => ImapSession.Quote(value, what);

    private static string RequireUid(string uid) => ImapSession.RequireUid(uid);

    [GeneratedRegex(@"UIDVALIDITY\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UidValidityPattern();

    [GeneratedRegex(@"^\*\s+SEARCH((?:\s+\d+)*)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SearchResultPattern();

    [GeneratedRegex(@"\bUID\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UidPattern();

    [GeneratedRegex(@"RFC822\.SIZE\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizePattern();

    [GeneratedRegex(@"INTERNALDATE\s+""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InternalDatePattern();
}
