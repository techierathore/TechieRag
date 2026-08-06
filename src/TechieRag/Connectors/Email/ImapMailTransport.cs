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
/// year of mail as read would be an unrecoverable act on someone's inbox.</para>
/// <para><b>Searching happens on the server.</b> Date, sender and subject are IMAP <c>SEARCH</c>
/// keys, so the scope filters are evaluated before anything is transferred. Only the UIDs of
/// matching messages come back, and headers are fetched a page at a time from that list.</para>
/// <para><b>Nothing is logged that could identify a credential.</b> Command text is never logged,
/// because the <c>LOGIN</c> and <c>AUTHENTICATE</c> commands are command text.</para>
/// <para><b>Nothing a caller supplies reaches the wire unchecked.</b> IMAP is a line protocol, so a
/// folder name, filter or account name carrying a carriage return is not a badly-formed argument —
/// it is a second command, executed with this account's credentials. Every such value is refused
/// before it is sent, and <see cref="RunAsync"/> refuses a composed command line containing a line
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
    public const int MaxResponseLines = 100_000;

    private readonly Func<IImapConnection> connectionFactory;
    private readonly ImapMailboxOptions options;
    private readonly ILogger<ImapMailTransport> logger;
    private readonly Dictionary<string, List<string>> searchCache = new(StringComparer.Ordinal);
    private IImapConnection? connection;
    private int tagCounter;
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
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? NullLogger<ImapMailTransport>.Instance;
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
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var result = await RunAsync("LIST \"\" \"*\"", cancellationToken).ConfigureAwait(false);
        Ensure(result, "list folders");

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
        var result = await RunAsync(command, cancellationToken).ConfigureAwait(false);
        Ensure(result, $"read headers from '{folder}'");

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
        var result = await RunAsync($"UID FETCH {uid} (BODY.PEEK[])", cancellationToken).ConfigureAwait(false);
        Ensure(result, $"read message {header.Uid}");

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
    public void Dispose()
    {
        connection?.Dispose();
        connection = null;
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (connection is not null)
        {
            return;
        }

        var opened = connectionFactory()
            ?? throw new ConnectorException("email", "The IMAP connection factory returned nothing.");

        connection = opened;
        await opened.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Checked before a single credential byte is written. BRD-135 requires plaintext to be
        // refused rather than warned about, and this is the point at which refusing still helps.
        if (!opened.IsSecure)
        {
            connection = null;
            opened.Dispose();
            throw new ConnectorException(
                "email",
                $"{options.Host} did not establish an encrypted session. Plaintext IMAP is refused; use port 993.");
        }

        await AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Authenticated to {Host} as {User}", options.Host, options.Username);
    }

    private async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        ImapResult result;

        if (options.UseOAuthBearer)
        {
            // The SASL XOAUTH2 initial response. Gmail and Microsoft 365 accept nothing else over
            // IMAP any more, so a password-only client cannot read the two largest mail providers.
            // The U+0001 separators are part of the mechanism rather than formatting: the server
            // splits the decoded blob on them, and a payload joined any other way authenticates
            // as nobody while reporting only a generic failure.
            RequireNoControlCharacters(options.Username, "account name");
            RequireNoControlCharacters(options.Password, "access token");

            var payload = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"user={options.Username}\u0001auth=Bearer {options.Password}\u0001\u0001"));

            result = await RunAsync($"AUTHENTICATE XOAUTH2 {payload}", cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await RunAsync(
                $"LOGIN {Quote(options.Username, "account name")} {Quote(options.Password, "password")}",
                cancellationToken).ConfigureAwait(false);
        }

        if (result.IsOk)
        {
            return;
        }

        // The server's own text is not repeated: it can echo the account name and, on some servers,
        // part of the credential.
        throw new ConnectorException(
            "email",
            $"{options.Host} rejected the credentials supplied for '{options.Username}'. Check the password or token, and whether the account requires an app password.",
            401);
    }

    private async Task SelectAsync(string folder, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        if (string.Equals(selectedFolder, folder, StringComparison.Ordinal))
        {
            return;
        }

        var result = await RunAsync($"SELECT {Quote(folder, "folder name")}", cancellationToken).ConfigureAwait(false);
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

        var result = await RunAsync($"UID SEARCH {BuildSearchKeys(criteria)}", cancellationToken).ConfigureAwait(false);
        Ensure(result, $"search '{folder}'");

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

    private async Task<ImapResult> RunAsync(string command, CancellationToken cancellationToken)
    {
        var pipe = connection ?? throw new ConnectorException("email", "The IMAP connection is not open.");
        var tag = $"T{++tagCounter:D4}";

        // The last line of defence against command injection. Every value that reaches a command is
        // checked at the point it is composed, where the failure can name what was wrong; this
        // catches the call site that forgets, and it costs one scan of a short string.
        RequireNoControlCharacters(command, "command");

        // Deliberately not logged: LOGIN and AUTHENTICATE commands carry the credential inline.
        await pipe.WriteLineAsync($"{tag} {command}", cancellationToken).ConfigureAwait(false);

        var lines = new List<ImapLine>();
        var continuations = 0;
        var literalBudget = options.MaxMessageBytes;

        while (true)
        {
            if (lines.Count > MaxResponseLines)
            {
                throw new ConnectorException(
                    "email",
                    $"{options.Host} sent more than {MaxResponseLines} untagged lines without completing the command. The connection was dropped.");
            }

            var raw = await pipe.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                      ?? throw new ConnectorException("email", $"{options.Host} closed the connection mid-command.");

            // A "+" line is the server asking for more input. The only case reached here is a failed
            // SASL exchange, which the RFC says to end by sending an empty line.
            if (raw.StartsWith("+ ", StringComparison.Ordinal) || raw == "+")
            {
                if (++continuations > 3)
                {
                    throw new ConnectorException("email", $"{options.Host} kept asking for input this client cannot supply.");
                }

                await pipe.WriteLineAsync(string.Empty, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var text = new StringBuilder();
            var literals = new List<byte[]>();
            var current = raw;

            // A trailing {n} means exactly n bytes follow, newlines and all. Reading them by count
            // is the only way to keep the frame; scanning for a newline would read into the body.
            while (TryReadLiteralLength(current, out var length))
            {
                // The length is the server's claim, and it is acted on by allocating before a byte
                // has been read. A declared {2000000000} is a two-gigabyte allocation the server
                // chose, so it is checked against a budget rather than honoured.
                if (length > literalBudget)
                {
                    throw new ConnectorException(
                        "email",
                        $"{options.Host} announced {length:N0} bytes of message content, beyond the {options.MaxMessageBytes:N0}-byte limit for one response. Raise ImapMailboxOptions.MaxMessageBytes if this mailbox genuinely holds mail that large.");
                }

                literalBudget -= length;
                text.Append(current);
                literals.Add(await pipe.ReadExactAsync(length, cancellationToken).ConfigureAwait(false));
                current = await pipe.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                          ?? throw new ConnectorException("email", $"{options.Host} closed the connection inside a literal.");
            }

            text.Append(current);
            var full = text.ToString();

            if (full.StartsWith(tag + " ", StringComparison.Ordinal))
            {
                return new ImapResult(
                    full.StartsWith($"{tag} OK", StringComparison.OrdinalIgnoreCase), full, lines);
            }

            lines.Add(new ImapLine(full, literals));
        }
    }

    private void Ensure(ImapResult result, string what)
    {
        if (!result.IsOk)
        {
            throw new ConnectorException("email", $"{options.Host} refused to {what}.");
        }
    }

    /// <summary>Reads the byte count of a trailing IMAP literal.</summary>
    /// <param name="line">A response line.</param>
    /// <param name="length">The literal's length when one is present.</param>
    /// <returns>True when the line ends in a literal marker.</returns>
    internal static bool TryReadLiteralLength(string line, out int length)
    {
        length = 0;
        var match = LiteralPattern().Match(line);
        return match.Success
               && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out length);
    }

    /// <summary>Splits an IMAP response fragment into quoted strings and bare atoms.</summary>
    /// <param name="value">The fragment.</param>
    /// <returns>The tokens, with quotes removed and escapes resolved.</returns>
    internal static List<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (inQuotes && character == '\\' && index + 1 < value.Length)
            {
                current.Append(value[++index]);
                continue;
            }

            if (character == '"')
            {
                if (inQuotes)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

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

    /// <summary>Renders a caller-supplied value as an IMAP quoted string.</summary>
    /// <param name="value">The value.</param>
    /// <param name="what">What the value is, for the failure message.</param>
    /// <returns>The value, escaped and quoted.</returns>
    /// <exception cref="ConnectorException">The value contains a control character.</exception>
    /// <remarks>
    /// <para>Escaping <c>\</c> and <c>"</c> keeps the quoted string well-formed; it does nothing at
    /// all about a line break, and a line break is the whole attack. IMAP frames commands by line,
    /// so <c>INBOX\r\nT9 STORE 1:* +FLAGS (\Deleted)</c> as a folder name is not a folder with an
    /// odd name — it is a delete issued with this account's credentials, on a connector whose entire
    /// read-only promise rests on only ever sending <c>BODY.PEEK</c>.</para>
    /// <para>The check is on control characters rather than on CR and LF alone: U+0001 is the field
    /// separator inside the <c>XOAUTH2</c> SASL payload, where it lets a supplied account name forge
    /// the token field. No folder, mailbox, account name or search term legitimately contains any of
    /// them, so refusing the value outright loses nothing and needs no escaping rules to be right.</para>
    /// </remarks>
    private static string Quote(string value, string what)
    {
        RequireNoControlCharacters(value, what);
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static void RequireNoControlCharacters(string value, string what)
    {
        foreach (var character in value)
        {
            if (!char.IsControl(character))
            {
                continue;
            }

            throw new ConnectorException(
                "email",
                $"The {what} contains a control character (U+{(int)character:X4}), which IMAP would read as the end of the command line. Remove it and try again.");
        }
    }

    /// <summary>Checks that a value is an IMAP UID before it is spliced into a command.</summary>
    /// <param name="uid">The UID from a <see cref="MailHeader"/>.</param>
    /// <returns>The UID.</returns>
    /// <exception cref="ConnectorException">The value is not a bare positive integer.</exception>
    /// <remarks>
    /// A UID is a number in the protocol and is never quoted, so unlike a folder name there is no
    /// escaping that would make an arbitrary string safe here. A header built by hand — or by a
    /// different transport, whose UIDs are Message-IDs — must not be able to append a command.
    /// </remarks>
    private static string RequireUid(string uid)
    {
        if (uid.Length == 0 || uid.Length > 20 || !uid.All(char.IsAsciiDigit))
        {
            throw new ConnectorException(
                "email", "A message identifier that is not a bare IMAP UID cannot be fetched over IMAP.");
        }

        return uid;
    }

    [GeneratedRegex(@"\{(\d+)\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex LiteralPattern();

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

    private sealed record ImapLine(string Text, IReadOnlyList<byte[]> Literals);

    private sealed record ImapResult(bool IsOk, string Completion, IReadOnlyList<ImapLine> Lines);
}
