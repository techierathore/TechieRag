using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace TechieRag.Connectors.Email;

/// <summary>
/// One authenticated IMAP conversation: connect, refuse plaintext, log in, send tagged commands and
/// read their bounded responses (REQ-RAG-049 / BRD-135, shared with REQ-RAG-110 / BRD-169).
/// </summary>
/// <remarks>
/// <para><b>Why this is shared, and why it carries a mode.</b> The reading transport
/// (<see cref="ImapMailTransport"/>) and the mail actions (<see cref="ImapMailActions"/>) log in the
/// same way and must refuse the same injected input, so the login, the framing and the guards live
/// here once. What differs is whether the conversation may change the mailbox at all, and that is
/// decided when the session is created, not by each call site: a read-only session refuses every
/// command outside a fixed allowlist of non-mutating verbs, so the reading connector and the dry run
/// cannot change a mailbox even through a future bug in the code that composes their commands.</para>
/// <para><b>Never a plain <c>EXPUNGE</c> or <c>CLOSE</c>, in either mode.</b> Both purge every
/// message in the folder that carries <c>\Deleted</c>, including ones the user flagged in their own
/// client and meant to keep recoverable. A writable session can only remove a message with
/// <c>UID EXPUNGE</c> of named UIDs (RFC 4315).</para>
/// </remarks>
internal sealed partial class ImapSession : IDisposable
{
    /// <summary>The most untagged response lines one command may produce before the server is judged hostile.</summary>
    internal const int MaxResponseLines = 100_000;

    /// <summary>The verbs a read-only session may send. Everything else is refused before it reaches the wire.</summary>
    private static readonly HashSet<string> ReadOnlyVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "CAPABILITY", "LOGIN", "AUTHENTICATE", "LIST", "LSUB", "STATUS", "SELECT", "EXAMINE", "SEARCH", "FETCH", "NOOP", "LOGOUT",
    };

    /// <summary>The verbs no session sends, because each purges or destroys mail beyond the named message.</summary>
    private static readonly HashSet<string> ForbiddenVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "EXPUNGE", "CLOSE", "DELETE", "APPEND",
    };

    private readonly Func<IImapConnection> connectionFactory;
    private readonly ImapMailboxOptions options;
    private readonly ILogger logger;
    private readonly bool readOnly;
    private IImapConnection? connection;
    private int tagCounter;

    /// <summary>Initializes a new instance of the <see cref="ImapSession"/> class.</summary>
    /// <param name="connectionFactory">Creates the byte pipe to the server.</param>
    /// <param name="options">Server address and credentials.</param>
    /// <param name="logger">Diagnostics. Never receives command text.</param>
    /// <param name="readOnly">True to refuse every command that could change the mailbox.</param>
    internal ImapSession(Func<IImapConnection> connectionFactory, ImapMailboxOptions options, ILogger logger, bool readOnly)
    {
        this.connectionFactory = connectionFactory;
        this.options = options;
        this.logger = logger;
        this.readOnly = readOnly;
    }

    /// <summary>Gets the server host name, for failure messages.</summary>
    internal string Host => options.Host;

    /// <summary>Gets a value indicating whether this session refuses mutating commands.</summary>
    internal bool IsReadOnly => readOnly;

    /// <summary>Opens the connection and authenticates, once.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the session is authenticated.</returns>
    internal async Task EnsureConnectedAsync(CancellationToken cancellationToken)
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

    /// <summary>Sends one tagged command and reads its response.</summary>
    /// <param name="command">The command without its tag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The untagged lines and the completion.</returns>
    /// <exception cref="ConnectorException">The command was refused locally, or the server misbehaved.</exception>
    internal async Task<ImapResult> RunAsync(string command, CancellationToken cancellationToken)
    {
        var pipe = connection ?? throw new ConnectorException("email", "The IMAP connection is not open.");

        // The last line of defence against command injection. Every value that reaches a command is
        // checked at the point it is composed, where the failure can name what was wrong; this
        // catches the call site that forgets, and it costs one scan of a short string.
        RequireNoControlCharacters(command, "command");
        RequireAllowedVerb(command);

        var tag = $"T{++tagCounter:D4}";

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
                        $"{options.Host} announced {length:N0} bytes of message content, beyond the {options.MaxMessageBytes:N0}-byte limit for one response. Raise ImapMailboxOptions.MaxMessageBytes if this mailbox genuinely holds mail that large.")
                    {
                        ErrorCode = ConnectorErrorCodes.ImapLiteralTooLarge,
                    };
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

    /// <summary>Throws when a command did not complete with OK.</summary>
    /// <param name="result">The command's result.</param>
    /// <param name="what">What was being attempted, for the message.</param>
    /// <exception cref="ConnectorException">The server refused the command.</exception>
    internal void Ensure(ImapResult result, string what)
    {
        if (!result.IsOk)
        {
            throw new ConnectorException("email", $"{options.Host} refused to {what}.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        connection?.Dispose();
        connection = null;
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

    /// <summary>Renders a caller-supplied value as an IMAP quoted string.</summary>
    /// <param name="value">The value.</param>
    /// <param name="what">What the value is, for the failure message.</param>
    /// <returns>The value, escaped and quoted.</returns>
    /// <exception cref="ConnectorException">The value contains a control character.</exception>
    /// <remarks>
    /// <para>Escaping <c>\</c> and <c>"</c> keeps the quoted string well-formed; it does nothing at
    /// all about a line break, and a line break is the whole attack. IMAP frames commands by line,
    /// so <c>INBOX\r\nT9 STORE 1:* +FLAGS (\Deleted)</c> as a folder name is not a folder with an
    /// odd name — it is a delete issued with this account's credentials.</para>
    /// <para>The check is on control characters rather than on CR and LF alone: U+0001 is the field
    /// separator inside the <c>XOAUTH2</c> SASL payload, where it lets a supplied account name forge
    /// the token field. No folder, label, mailbox, account name or search term legitimately contains
    /// any of them, so refusing the value outright loses nothing and needs no escaping rules to be right.</para>
    /// </remarks>
    internal static string Quote(string value, string what)
    {
        RequireNoControlCharacters(value, what);
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>Refuses a value that contains any control character.</summary>
    /// <param name="value">The value.</param>
    /// <param name="what">What the value is, for the failure message.</param>
    /// <exception cref="ConnectorException">The value contains a control character.</exception>
    internal static void RequireNoControlCharacters(string value, string what)
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
    /// <param name="uid">The UID.</param>
    /// <returns>The UID.</returns>
    /// <exception cref="ConnectorException">The value is not a bare positive integer.</exception>
    /// <remarks>
    /// A UID is a number in the protocol and is never quoted, so unlike a folder name there is no
    /// escaping that would make an arbitrary string safe here. A header built by hand — or by a
    /// different transport, whose UIDs are Message-IDs — must not be able to append a command.
    /// </remarks>
    internal static string RequireUid(string uid)
    {
        if (uid.Length == 0 || uid.Length > 20 || !uid.All(char.IsAsciiDigit))
        {
            throw new ConnectorException(
                "email", "A message identifier that is not a bare IMAP UID cannot be used over IMAP.");
        }

        return uid;
    }

    private void RequireAllowedVerb(string command)
    {
        var tokens = command.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw new ConnectorException("email", "An empty IMAP command was refused.");
        }

        var verb = string.Equals(tokens[0], "UID", StringComparison.OrdinalIgnoreCase) && tokens.Length > 1
            ? tokens[1]
            : tokens[0];

        // UID EXPUNGE names its messages and is the one removal this library may send; a bare
        // EXPUNGE, CLOSE, DELETE or APPEND never leaves this method.
        var namedExpunge = string.Equals(tokens[0], "UID", StringComparison.OrdinalIgnoreCase)
                           && string.Equals(verb, "EXPUNGE", StringComparison.OrdinalIgnoreCase)
                           && tokens.Length == 3;
        if (ForbiddenVerbs.Contains(verb) && !namedExpunge)
        {
            throw new ConnectorException("email", $"The IMAP command {verb.ToUpperInvariant()} is never sent by this library.");
        }

        if (!readOnly)
        {
            return;
        }

        // A read-only session also refuses a non-PEEK body fetch, which would mark the message read.
        if (!ReadOnlyVerbs.Contains(verb) || command.Contains("BODY[", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConnectorException(
                "email", $"The IMAP command {verb.ToUpperInvariant()} would change the mailbox and was refused by a read-only session.");
        }
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

    [GeneratedRegex(@"\{(\d+)\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex LiteralPattern();
}
