using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Connectors.Email;

/// <summary>
/// Acts on mail over the same IMAP login the email connector reads with: move to a folder, add or
/// remove a Gmail label, move to Trash; with a dry run that changes nothing
/// (REQ-RAG-110 / BRD-169, REQ-RAG-111 / BRD-170).
/// </summary>
/// <remarks>
/// <para><b>A separate type, on purpose.</b> <see cref="ImapMailTransport"/> stays read-only by
/// construction, so ingesting a mailbox can never change it. Acting on mail is an explicit choice a
/// host makes by creating this type with the same <see cref="ImapMailboxOptions"/> (password, Gmail
/// app password or XOAUTH2 token).</para>
/// <para><b>Nothing is deleted.</b> Trash moves the message to the folder the server marks
/// <c>\Trash</c> (RFC 6154 special-use), or Gmail's <c>[Gmail]/Trash</c>, so a mistaken action is
/// recoverable from the user's own client. A move uses <c>UID MOVE</c> (RFC 6851) when the server
/// offers it; otherwise, with <c>UIDPLUS</c>, <c>UID COPY</c> then <c>\Deleted</c> and
/// <c>UID EXPUNGE</c> of that one UID. A plain <c>EXPUNGE</c> would also purge every other message the
/// user had flagged deleted, so a server offering neither is refused with
/// <see cref="MailActionCodes.MoveNotSupported"/> and the session refuses to send one at all.</para>
/// <para><b>Labels are Gmail's.</b> <c>X-GM-LABELS</c> when the server advertises
/// <c>X-GM-EXT-1</c>; anywhere else <see cref="MailActionCodes.LabelsNotSupported"/>. An IMAP keyword
/// would look as if it worked while the user's client never showed it.</para>
/// <para><b>One result per action.</b> A message that cannot be acted on fails alone with a stable
/// code; the others carry on. A run-level problem before any action is attempted (plaintext refused,
/// credentials rejected) throws <see cref="ConnectorException"/> as the reader does.</para>
/// <para><b>The dry run cannot change the mailbox.</b> <see cref="DryRunAsync"/> uses a read-only
/// session that refuses every command but <c>CAPABILITY</c>, <c>LOGIN</c>/<c>AUTHENTICATE</c>,
/// <c>LIST</c>, <c>EXAMINE</c>, <c>SEARCH</c> and <c>FETCH</c>-family reads, and opens folders with
/// <c>EXAMINE</c>, so the plan's own checks leave even the <c>\Recent</c> flags alone.</para>
/// <para>Each call opens its own connection and closes it on return. Names (folders, labels) are
/// passed as the server lists them; nothing a caller supplies reaches the wire with a control
/// character in it.</para>
/// </remarks>
public sealed partial class ImapMailActions
{
    /// <summary>The Trash folder used on Gmail when the server lists no <c>\Trash</c> special-use folder.</summary>
    public const string GmailTrashFolder = "[Gmail]/Trash";

    private readonly Func<IImapConnection> connectionFactory;
    private readonly ImapMailboxOptions options;
    private readonly ILogger<ImapMailActions> logger;

    /// <summary>Initializes a new instance of the <see cref="ImapMailActions"/> class.</summary>
    /// <param name="connectionFactory">Creates the byte pipe to the server. Tests pass a scripted fake; production passes <see cref="SocketImapConnection"/>.</param>
    /// <param name="options">Server address and credentials, the same ones the reader uses.</param>
    /// <param name="logger">Diagnostics. Never receives command text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="options"/> is null.</exception>
    public ImapMailActions(
        Func<IImapConnection> connectionFactory,
        ImapMailboxOptions options,
        ILogger<ImapMailActions>? logger = null)
    {
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? NullLogger<ImapMailActions>.Instance;
    }

    /// <summary>Creates mail actions that connect to a real server over TLS.</summary>
    /// <param name="options">Server address and credentials.</param>
    /// <param name="logger">Diagnostics.</param>
    /// <returns>The mail actions.</returns>
    /// <exception cref="ArgumentException"><see cref="ImapMailboxOptions.Host"/> is empty.</exception>
    public static ImapMailActions Create(ImapMailboxOptions options, ILogger<ImapMailActions>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new ArgumentException("IMAP mail actions need a Host.", nameof(options));
        }

        return new ImapMailActions(
            () => new SocketImapConnection(options.Host, options.Port, options.Timeout), options, logger);
    }

    /// <summary>Carries out the actions, in order, and returns one result per action.</summary>
    /// <param name="actions">The actions. Several may share a folder; each is reported on its own.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One result per action, in the order given.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="actions"/> is null or contains null.</exception>
    /// <exception cref="ConnectorException">Plaintext was refused or the credentials were rejected.</exception>
    public async Task<IReadOnlyList<MailActionResult>> ApplyAsync(
        IEnumerable<MailAction> actions,
        CancellationToken cancellationToken = default)
    {
        var entries = await ProcessAsync(actions, apply: true, cancellationToken).ConfigureAwait(false);
        return entries
            .Select(entry => new MailActionResult(entry.Action, entry.Outcome, entry.Strategy, entry.TargetFolder, entry.Code, entry.Reason))
            .ToList();
    }

    /// <summary>Plans the actions against the live mailbox without changing it (REQ-RAG-111 / BRD-170).</summary>
    /// <param name="actions">The actions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One plan per action, in the order given, with the commands that would be sent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="actions"/> is null or contains null.</exception>
    /// <exception cref="ConnectorException">Plaintext was refused or the credentials were rejected.</exception>
    public async Task<IReadOnlyList<MailActionPlan>> DryRunAsync(
        IEnumerable<MailAction> actions,
        CancellationToken cancellationToken = default)
    {
        var entries = await ProcessAsync(actions, apply: false, cancellationToken).ConfigureAwait(false);
        return entries
            .Select(entry => new MailActionPlan(
                entry.Action, entry.Outcome, entry.Strategy, entry.TargetFolder, entry.Commands, entry.Code, entry.Reason))
            .ToList();
    }

    /// <summary>Moves messages to a folder.</summary>
    /// <param name="messages">The messages.</param>
    /// <param name="folder">The destination folder; it must exist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One result per message.</returns>
    public Task<IReadOnlyList<MailActionResult>> MoveAsync(
        IEnumerable<MailMessageRef> messages,
        string folder,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(Each(messages, message => MailAction.Move(message, folder)), cancellationToken);

    /// <summary>Moves messages to the server's Trash folder. Never a permanent delete.</summary>
    /// <param name="messages">The messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One result per message.</returns>
    public Task<IReadOnlyList<MailActionResult>> TrashAsync(
        IEnumerable<MailMessageRef> messages,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(Each(messages, MailAction.Trash), cancellationToken);

    /// <summary>Adds a Gmail label to messages.</summary>
    /// <param name="messages">The messages.</param>
    /// <param name="label">The label.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One result per message.</returns>
    public Task<IReadOnlyList<MailActionResult>> AddLabelAsync(
        IEnumerable<MailMessageRef> messages,
        string label,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(Each(messages, message => MailAction.AddLabel(message, label)), cancellationToken);

    /// <summary>Removes a Gmail label from messages.</summary>
    /// <param name="messages">The messages.</param>
    /// <param name="label">The label.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One result per message.</returns>
    public Task<IReadOnlyList<MailActionResult>> RemoveLabelAsync(
        IEnumerable<MailMessageRef> messages,
        string label,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(Each(messages, message => MailAction.RemoveLabel(message, label)), cancellationToken);

    /// <summary>Reads the attributes and name out of an untagged LIST response.</summary>
    /// <param name="line">The response line.</param>
    /// <returns>The attributes and the name, or null when the line is not a LIST response.</returns>
    internal static (HashSet<string> Attributes, string Name)? ParseListEntry(string line)
    {
        if (!line.StartsWith("* LIST", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var open = line.IndexOf('(');
        var close = line.IndexOf(')');
        if (open < 0 || close < open)
        {
            return null;
        }

        var attributes = new HashSet<string>(
            line[(open + 1)..close].Split(' ', StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
        var tokens = ImapSession.Tokenize(line[(close + 1)..].Trim());
        return tokens.Count < 2 ? null : (attributes, tokens[^1]);
    }

    private static IEnumerable<MailAction> Each(IEnumerable<MailMessageRef> messages, Func<MailMessageRef, MailAction> create)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return messages.Select(create).ToList();
    }

    private async Task<IReadOnlyList<Entry>> ProcessAsync(
        IEnumerable<MailAction> actions,
        bool apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var entries = actions.Select(action => new Entry(action ?? throw new ArgumentNullException(nameof(actions)))).ToList();
        foreach (var entry in entries)
        {
            Validate(entry);
        }

        if (entries.All(entry => entry.IsDecided))
        {
            return entries;
        }

        // The dry run's session is read-only: a mutating command cannot leave it, whatever this
        // method composes.
        using var session = new ImapSession(connectionFactory, options, logger, readOnly: !apply);
        await session.EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var capabilities = await ReadCapabilitiesAsync(session, cancellationToken).ConfigureAwait(false);
            var needsFolders = entries.Any(entry => !entry.IsDecided && entry.Action.Kind is MailActionKind.Move or MailActionKind.Trash);
            var folders = needsFolders
                ? await ListFoldersAsync(session, cancellationToken).ConfigureAwait(false)
                : [];
            var trash = ResolveTrash(folders, capabilities);

            foreach (var group in entries.Where(entry => !entry.IsDecided).GroupBy(entry => entry.Action.Message.Folder, StringComparer.Ordinal))
            {
                await ProcessFolderAsync(session, group.Key, group.ToList(), capabilities, folders, trash, apply, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (ConnectorException error)
        {
            // Results already decided stand; the rest are reported, not lost.
            foreach (var entry in entries.Where(entry => !entry.IsDecided))
            {
                entry.Fail(MailActionCodes.ConnectionLost, error.Message);
            }
        }

        foreach (var entry in entries)
        {
            logger.LogInformation(
                "Mail action {Kind} on UID {Uid}: {Outcome} {Code}",
                entry.Action.Kind,
                entry.Action.Message.Uid,
                entry.Outcome,
                entry.Code);
        }

        return entries;
    }

    private static void Validate(Entry entry)
    {
        var action = entry.Action;
        try
        {
            ArgumentNullException.ThrowIfNull(action.Message);
            if (string.IsNullOrEmpty(action.Message.Folder))
            {
                throw new ConnectorException("email", "The message's folder is empty.");
            }

            ImapSession.RequireNoControlCharacters(action.Message.Folder, "folder name");
            ImapSession.RequireUid(action.Message.Uid ?? string.Empty);

            if (action.Kind != MailActionKind.Trash)
            {
                if (string.IsNullOrWhiteSpace(action.Target))
                {
                    throw new ConnectorException(
                        "email", action.Kind == MailActionKind.Move ? "A move needs a destination folder." : "A label action needs a label.");
                }

                ImapSession.RequireNoControlCharacters(action.Target, action.Kind == MailActionKind.Move ? "folder name" : "label");
            }
        }
        catch (Exception error) when (error is ConnectorException or ArgumentException)
        {
            entry.Fail(MailActionCodes.InvalidArgument, error.Message);
        }
    }

    private static async Task<HashSet<string>> ReadCapabilitiesAsync(ImapSession session, CancellationToken cancellationToken)
    {
        var result = await session.RunAsync("CAPABILITY", cancellationToken).ConfigureAwait(false);
        session.Ensure(result, "report its capabilities");

        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in result.Lines)
        {
            if (line.Text.StartsWith("* CAPABILITY ", StringComparison.OrdinalIgnoreCase))
            {
                capabilities.UnionWith(line.Text["* CAPABILITY ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return capabilities;
    }

    private static async Task<Dictionary<string, HashSet<string>>> ListFoldersAsync(ImapSession session, CancellationToken cancellationToken)
    {
        var result = await session.RunAsync("LIST \"\" \"*\"", cancellationToken).ConfigureAwait(false);
        session.Ensure(result, "list folders");

        var folders = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var line in result.Lines)
        {
            if (ParseListEntry(line.Text) is { } entry)
            {
                folders[entry.Name] = entry.Attributes;
            }
        }

        return folders;
    }

    private static string? ResolveTrash(Dictionary<string, HashSet<string>> folders, HashSet<string> capabilities)
    {
        foreach (var (name, attributes) in folders)
        {
            if (attributes.Contains("\\Trash"))
            {
                return name;
            }
        }

        return capabilities.Contains("X-GM-EXT-1") ? GmailTrashFolder : null;
    }

    private async Task ProcessFolderAsync(
        ImapSession session,
        string folder,
        List<Entry> group,
        HashSet<string> capabilities,
        Dictionary<string, HashSet<string>> folders,
        string? trash,
        bool apply,
        CancellationToken cancellationToken)
    {
        // EXAMINE in a dry run: a read-only open that does not even clear \Recent.
        var verb = apply ? "SELECT" : "EXAMINE";
        var opened = await session.RunAsync($"{verb} {ImapSession.Quote(folder, "folder name")}", cancellationToken).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            foreach (var entry in group)
            {
                entry.Fail(MailActionCodes.FolderNotFound, $"Folder '{folder}' could not be opened on {session.Host}.");
            }

            return;
        }

        var uidValidity = opened.Lines
            .Select(line => UidValidityPattern().Match(line.Text))
            .FirstOrDefault(match => match.Success)?.Groups[1].Value;

        var pending = new List<Entry>();
        foreach (var entry in group)
        {
            var expected = entry.Action.Message.UidValidity;
            if (!string.IsNullOrEmpty(expected) && expected != "0" && uidValidity is not null
                && !string.Equals(expected, uidValidity, StringComparison.Ordinal))
            {
                entry.Fail(
                    MailActionCodes.UidValidityChanged,
                    $"Folder '{folder}' now has UIDVALIDITY {uidValidity}, not {expected}; UID {entry.Action.Message.Uid} may name a different message.");
                continue;
            }

            pending.Add(entry);
        }

        if (pending.Count == 0)
        {
            return;
        }

        var present = await FindPresentAsync(session, pending, cancellationToken).ConfigureAwait(false);

        foreach (var entry in pending)
        {
            var uid = entry.Action.Message.Uid;
            if (!present.Contains(uid))
            {
                entry.Fail(MailActionCodes.MessageNotFound, $"No message with UID {uid} is in '{folder}'.");
                continue;
            }

            Plan(entry, folder, capabilities, folders, trash);
            if (entry.IsDecided)
            {
                continue;
            }

            if (!apply)
            {
                entry.Succeed();
                continue;
            }

            await ExecuteAsync(session, entry, cancellationToken).ConfigureAwait(false);
            if (entry.Outcome == MailActionOutcome.Done && entry.Strategy != MailMoveStrategy.None)
            {
                // The message has left this folder; a later action on the same UID here must not
                // report success against a message that is no longer there.
                present.Remove(uid);
            }
        }
    }

    private static async Task<HashSet<string>> FindPresentAsync(ImapSession session, List<Entry> pending, CancellationToken cancellationToken)
    {
        var uids = string.Join(",", pending.Select(entry => entry.Action.Message.Uid).Distinct(StringComparer.Ordinal));
        var result = await session.RunAsync($"UID SEARCH UID {uids}", cancellationToken).ConfigureAwait(false);
        session.Ensure(result, "search the folder");

        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in result.Lines)
        {
            var match = SearchResultPattern().Match(line.Text);
            if (!match.Success)
            {
                continue;
            }

            foreach (var token in match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var uid))
                {
                    present.Add(uid.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        // A UID supplied with leading zeros still names the message the server reports without them.
        foreach (var entry in pending)
        {
            if (long.TryParse(entry.Action.Message.Uid, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && present.Contains(number.ToString(CultureInfo.InvariantCulture)))
            {
                present.Add(entry.Action.Message.Uid);
            }
        }

        return present;
    }

    private static void Plan(
        Entry entry,
        string folder,
        HashSet<string> capabilities,
        Dictionary<string, HashSet<string>> folders,
        string? trash)
    {
        var action = entry.Action;
        var uid = action.Message.Uid;

        if (action.Kind is MailActionKind.AddLabel or MailActionKind.RemoveLabel)
        {
            if (!capabilities.Contains("X-GM-EXT-1"))
            {
                entry.Fail(
                    MailActionCodes.LabelsNotSupported,
                    "This server does not offer Gmail labels (X-GM-EXT-1); an IMAP keyword is never used instead.");
                return;
            }

            var sign = action.Kind == MailActionKind.AddLabel ? '+' : '-';
            entry.Commands.Add($"UID STORE {uid} {sign}X-GM-LABELS ({RenderLabel(action.Target!)})");
            return;
        }

        string target;
        if (action.Kind == MailActionKind.Trash)
        {
            if (trash is null)
            {
                entry.Fail(
                    MailActionCodes.TrashFolderNotFound,
                    "The server lists no \\Trash special-use folder and is not Gmail; the message was not deleted.");
                return;
            }

            target = trash;
        }
        else
        {
            target = action.Target!;
            if (!folders.TryGetValue(target, out var attributes)
                || attributes.Contains("\\Noselect")
                || attributes.Contains("\\NonExistent"))
            {
                entry.Fail(MailActionCodes.TargetFolderNotFound, $"Folder '{target}' does not exist; folders are never created.");
                return;
            }
        }

        entry.TargetFolder = target;
        if (SameFolder(folder, target))
        {
            entry.Skip(MailActionCodes.AlreadyInFolder, $"The message is already in '{target}'.");
            return;
        }

        var quoted = ImapSession.Quote(target, "folder name");
        if (capabilities.Contains("MOVE"))
        {
            entry.Strategy = MailMoveStrategy.Move;
            entry.Commands.Add($"UID MOVE {uid} {quoted}");
        }
        else if (capabilities.Contains("UIDPLUS"))
        {
            entry.Strategy = MailMoveStrategy.CopyAndExpunge;
            entry.Commands.Add($"UID COPY {uid} {quoted}");
            entry.Commands.Add($"UID STORE {uid} +FLAGS.SILENT (\\Deleted)");
            entry.Commands.Add($"UID EXPUNGE {uid}");
        }
        else
        {
            entry.Fail(
                MailActionCodes.MoveNotSupported,
                "The server offers neither MOVE nor UIDPLUS; moving would need a plain EXPUNGE, which this library never sends.");
        }
    }

    private static async Task ExecuteAsync(ImapSession session, Entry entry, CancellationToken cancellationToken)
    {
        for (var index = 0; index < entry.Commands.Count; index++)
        {
            var result = await session.RunAsync(entry.Commands[index], cancellationToken).ConfigureAwait(false);
            if (result.IsOk)
            {
                continue;
            }

            var said = ServerText(result.Completion);
            if (entry.Strategy == MailMoveStrategy.CopyAndExpunge && index > 0)
            {
                entry.Fail(
                    MailActionCodes.CopiedNotRemoved,
                    $"The message was copied to '{entry.TargetFolder}' but the original could not be removed: {said}");
            }
            else if (said.Contains("[TRYCREATE]", StringComparison.OrdinalIgnoreCase))
            {
                entry.Fail(MailActionCodes.TargetFolderNotFound, $"Folder '{entry.TargetFolder}' does not exist: {said}");
            }
            else
            {
                entry.Fail(MailActionCodes.ServerRefused, $"{session.Host} refused the action: {said}");
            }

            return;
        }

        entry.Succeed();
    }

    private static string RenderLabel(string label) =>
        SystemLabelPattern().IsMatch(label) ? label : ImapSession.Quote(label, "label");

    private static bool SameFolder(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal)
        || (string.Equals(left, "INBOX", StringComparison.OrdinalIgnoreCase) && string.Equals(right, "INBOX", StringComparison.OrdinalIgnoreCase));

    private static string ServerText(string completion)
    {
        var space = completion.IndexOf(' ');
        var text = space >= 0 ? completion[(space + 1)..] : completion;
        return text.Length > 200 ? text[..200] : text;
    }

    [GeneratedRegex(@"UIDVALIDITY\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UidValidityPattern();

    [GeneratedRegex(@"^\*\s+SEARCH((?:\s+\d+)*)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SearchResultPattern();

    [GeneratedRegex(@"^\\[A-Za-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SystemLabelPattern();

    /// <summary>The working state of one action while a batch runs.</summary>
    private sealed class Entry(MailAction action)
    {
        public MailAction Action { get; } = action;

        public MailActionOutcome Outcome { get; private set; } = MailActionOutcome.Done;

        public bool IsDecided { get; private set; }

        public MailMoveStrategy Strategy { get; set; }

        public string? TargetFolder { get; set; }

        public List<string> Commands { get; } = [];

        public string? Code { get; private set; }

        public string? Reason { get; private set; }

        public void Succeed()
        {
            Outcome = MailActionOutcome.Done;
            IsDecided = true;
        }

        public void Skip(string code, string reason) => Decide(MailActionOutcome.Skipped, code, reason);

        public void Fail(string code, string reason) => Decide(MailActionOutcome.Failed, code, reason);

        private void Decide(MailActionOutcome outcome, string code, string reason)
        {
            Outcome = outcome;
            Code = code;
            Reason = reason;
            IsDecided = true;
        }
    }
}
