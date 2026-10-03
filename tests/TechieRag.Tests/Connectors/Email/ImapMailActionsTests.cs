using TechieRag.Connectors;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors.Email;

/// <summary>
/// REQ-RAG-110 / BRD-169 and REQ-RAG-111 / BRD-170: mail actions over the reader's IMAP login,
/// driven by a scripted server so the exact commands on the wire can be asserted.
/// </summary>
/// <remarks>
/// The tagged numbering is the conversation: T0001 is the login, T0002 CAPABILITY, then LIST when a
/// move or Trash needs folders, then SELECT (EXAMINE in a dry run), UID SEARCH and the actions.
/// </remarks>
public sealed class ImapMailActionsTests
{
    private const string Gmail = "IMAP4rev1 UNSELECT IDLE NAMESPACE QUOTA ID XLIST CHILDREN X-GM-EXT-1 UIDPLUS COMPRESS=DEFLATE ENABLE MOVE CONDSTORE ESEARCH UTF8=ACCEPT LIST-EXTENDED LIST-STATUS LITERAL- SPECIAL-USE APPENDLIMIT=35651584";

    private const string Payload = "\r\nT0099 UID STORE 1:* +FLAGS (\\Deleted)";

    private static readonly string[] GmailFolders =
    [
        "* LIST (\\HasNoChildren) \"/\" \"INBOX\"",
        "* LIST (\\HasNoChildren) \"/\" \"Archive\"",
        "* LIST (\\HasChildren \\Noselect) \"/\" \"[Gmail]\"",
        "* LIST (\\All \\HasNoChildren) \"/\" \"[Gmail]/All Mail\"",
        "* LIST (\\HasNoChildren \\Trash) \"/\" \"[Gmail]/Trash\"",
    ];

    private static readonly string[] ExchangeFolders =
    [
        "* LIST (\\HasNoChildren) \".\" \"INBOX\"",
        "* LIST (\\HasNoChildren) \".\" \"Archive\"",
        "* LIST (\\HasNoChildren \\Trash) \".\" \"Deleted Items\"",
    ];

    /// <summary>
    /// Acceptance of REQ-RAG-110: on a Gmail mailbox, one message moved to Archive, one labelled and
    /// one trashed each go where they were sent and each has its own Done result.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-110 MoveLabelAndTrashGiveOneResultEach")]
    public async Task MoveLabelAndTrashGiveOneResultEach()
    {
        var connection = Session(Gmail, GmailFolders, "INBOX", "7 8 9")
            .Line("T0006 OK [COPYUID 1 7 101] moved")
            .Line("* 2 FETCH (X-GM-LABELS (Receipts) UID 8)")
            .Line("T0007 OK stored")
            .Line("T0008 OK [COPYUID 2 9 55] moved");

        var results = await Actions(connection).ApplyAsync(ThreeActions());

        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.Equal(MailActionOutcome.Done, result.Outcome));
        Assert.Equal("Archive", results[0].TargetFolder);
        Assert.Equal("[Gmail]/Trash", results[2].TargetFolder);
        Assert.Equal(
            ["UID MOVE 7 \"Archive\"", "UID STORE 8 +X-GM-LABELS (\"Receipts\")", "UID MOVE 9 \"[Gmail]/Trash\""],
            connection.Written.Skip(5).Select(Untagged).ToList());
    }

    /// <summary>A server that advertises MOVE (RFC 6851) gets one UID MOVE and nothing else for the message.</summary>
    [Fact(DisplayName = "REQ-RAG-110 MoveUsesUidMoveWhenAdvertised")]
    public async Task MoveUsesUidMoveWhenAdvertised()
    {
        var connection = Session("IMAP4rev1 MOVE UIDPLUS", ExchangeFolders, "INBOX", "7")
            .Line("T0006 OK moved");

        var results = await Actions(connection).MoveAsync([Message("7")], "Archive");

        Assert.Equal(MailActionOutcome.Done, results.Single().Outcome);
        Assert.Equal(MailMoveStrategy.Move, results.Single().Strategy);
        Assert.Equal("T0006 UID MOVE 7 \"Archive\"", connection.Written[^1]);
        Assert.DoesNotContain(connection.Written, line => line.Contains("COPY", StringComparison.Ordinal));
    }

    /// <summary>
    /// Without MOVE but with UIDPLUS the move is UID COPY, \Deleted on that UID, and UID EXPUNGE of
    /// that UID only. A bare EXPUNGE, which would purge every \Deleted message in the folder, is never sent.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-110 MoveFallsBackToCopyAndUidExpunge")]
    public async Task MoveFallsBackToCopyAndUidExpunge()
    {
        var connection = Session("IMAP4rev1 UIDPLUS", ExchangeFolders, "INBOX", "7")
            .Line("T0006 OK [COPYUID 1 7 300] copied")
            .Line("T0007 OK stored")
            .Line("* 1 EXPUNGE")
            .Line("T0008 OK expunged");

        var results = await Actions(connection).MoveAsync([Message("7")], "Archive");

        Assert.Equal(MailActionOutcome.Done, results.Single().Outcome);
        Assert.Equal(MailMoveStrategy.CopyAndExpunge, results.Single().Strategy);
        Assert.Equal(
            ["UID COPY 7 \"Archive\"", "UID STORE 7 +FLAGS.SILENT (\\Deleted)", "UID EXPUNGE 7"],
            connection.Written.Skip(5).Select(Untagged).ToList());
        Assert.DoesNotContain(connection.Written, line => Untagged(line).StartsWith("EXPUNGE", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A server with neither MOVE nor UIDPLUS is refused with a code, and nothing that changes the mailbox is sent.</summary>
    [Fact(DisplayName = "REQ-RAG-110 MoveRefusedWithoutMoveOrUidplus")]
    public async Task MoveRefusedWithoutMoveOrUidplus()
    {
        var connection = Session("IMAP4rev1", ExchangeFolders, "INBOX", "7");

        var results = await Actions(connection).ApplyAsync([MailAction.Move(Message("7"), "Archive"), MailAction.Trash(Message("7"))]);

        Assert.All(results, result =>
        {
            Assert.Equal(MailActionOutcome.Failed, result.Outcome);
            Assert.Equal(MailActionCodes.MoveNotSupported, result.Code);
        });
        AssertNoMutatingCommand(connection);
    }

    /// <summary>Trash is the folder the server marks \Trash (RFC 6154), whatever it is called.</summary>
    [Fact(DisplayName = "REQ-RAG-110 TrashUsesSpecialUseFolder")]
    public async Task TrashUsesSpecialUseFolder()
    {
        var connection = Session("IMAP4rev1 MOVE", ExchangeFolders, "INBOX", "9")
            .Line("T0006 OK moved");

        var results = await Actions(connection).TrashAsync([Message("9")]);

        Assert.Equal(MailActionOutcome.Done, results.Single().Outcome);
        Assert.Equal("Deleted Items", results.Single().TargetFolder);
        Assert.Equal("T0006 UID MOVE 9 \"Deleted Items\"", connection.Written[^1]);
    }

    /// <summary>On Gmail with no \Trash attribute listed, Trash is [Gmail]/Trash.</summary>
    [Fact(DisplayName = "REQ-RAG-110 TrashFallsBackToGmailTrash")]
    public async Task TrashFallsBackToGmailTrash()
    {
        string[] plainFolders = ["* LIST (\\HasNoChildren) \"/\" \"INBOX\""];
        var connection = Session("IMAP4rev1 X-GM-EXT-1 MOVE", plainFolders, "INBOX", "9")
            .Line("T0006 OK moved");

        var results = await Actions(connection).TrashAsync([Message("9")]);

        Assert.Equal(ImapMailActions.GmailTrashFolder, results.Single().TargetFolder);
        Assert.Equal("T0006 UID MOVE 9 \"[Gmail]/Trash\"", connection.Written[^1]);
    }

    /// <summary>A non-Gmail server with no \Trash folder refuses Trash with a code; the message is never deleted.</summary>
    [Fact(DisplayName = "REQ-RAG-110 TrashRefusedWithoutTrashFolder")]
    public async Task TrashRefusedWithoutTrashFolder()
    {
        string[] plainFolders = ["* LIST (\\HasNoChildren) \".\" \"INBOX\""];
        var connection = Session("IMAP4rev1 MOVE UIDPLUS", plainFolders, "INBOX", "9");

        var result = (await Actions(connection).TrashAsync([Message("9")])).Single();

        Assert.Equal(MailActionCodes.TrashFolderNotFound, result.Code);
        AssertNoMutatingCommand(connection);
    }

    /// <summary>A message already in Trash is skipped, not moved onto itself.</summary>
    [Fact(DisplayName = "REQ-RAG-110 TrashSkipsMessageAlreadyInTrash")]
    public async Task TrashSkipsMessageAlreadyInTrash()
    {
        var connection = Session(Gmail, GmailFolders, "[Gmail]/Trash", "9");

        var result = (await Actions(connection).TrashAsync([new MailMessageRef("[Gmail]/Trash", "9")])).Single();

        Assert.Equal(MailActionOutcome.Skipped, result.Outcome);
        Assert.Equal(MailActionCodes.AlreadyInFolder, result.Code);
        AssertNoMutatingCommand(connection);
    }

    /// <summary>Gmail labels are added and removed with X-GM-LABELS; system labels go bare, user labels quoted.</summary>
    [Fact(DisplayName = "REQ-RAG-110 LabelAddAndRemoveOnGmail")]
    public async Task LabelAddAndRemoveOnGmail()
    {
        var connection = new ScriptedImapConnection()
            .Line("* OK ready")
            .Line("T0001 OK logged in")
            .Line("* CAPABILITY " + Gmail)
            .Line("T0002 OK done")
            .Line("* OK [UIDVALIDITY 11] valid")
            .Line("T0003 OK [READ-WRITE] selected")
            .Line("* SEARCH 7 8")
            .Line("T0004 OK done")
            .Line("T0005 OK stored")
            .Line("T0006 OK stored");

        var results = await Actions(connection).ApplyAsync(
            [MailAction.AddLabel(Message("7"), "Clients/Acme"), MailAction.RemoveLabel(Message("8"), "\\Starred")]);

        Assert.All(results, result => Assert.Equal(MailActionOutcome.Done, result.Outcome));
        Assert.Equal("T0005 UID STORE 7 +X-GM-LABELS (\"Clients/Acme\")", connection.Written[4]);
        Assert.Equal("T0006 UID STORE 8 -X-GM-LABELS (\\Starred)", connection.Written[5]);
        Assert.DoesNotContain(connection.Written, line => line.StartsWith("T0003 LIST", StringComparison.Ordinal));
    }

    /// <summary>A label on a server without Gmail's extension is a coded refusal, never an IMAP keyword.</summary>
    [Fact(DisplayName = "REQ-RAG-110 LabelRefusedOnNonGmail")]
    public async Task LabelRefusedOnNonGmail()
    {
        var connection = new ScriptedImapConnection()
            .Line("* OK ready")
            .Line("T0001 OK logged in")
            .Line("* CAPABILITY IMAP4rev1 MOVE UIDPLUS")
            .Line("T0002 OK done")
            .Line("* OK [UIDVALIDITY 11] valid")
            .Line("T0003 OK selected")
            .Line("* SEARCH 7")
            .Line("T0004 OK done");

        var result = (await Actions(connection).AddLabelAsync([Message("7")], "Receipts")).Single();

        Assert.Equal(MailActionOutcome.Failed, result.Outcome);
        Assert.Equal(MailActionCodes.LabelsNotSupported, result.Code);
        Assert.DoesNotContain(connection.Written, line => line.Contains("STORE", StringComparison.Ordinal));
    }

    /// <summary>
    /// One message the server refuses fails alone: the other two of three are still done, and each
    /// result carries its own outcome.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-110 OneFailingMessageAmongThree")]
    public async Task OneFailingMessageAmongThree()
    {
        var connection = Session("IMAP4rev1 MOVE", ExchangeFolders, "INBOX", "7 8 9")
            .Line("T0006 OK moved")
            .Line("T0007 NO [CANNOT] message is locked")
            .Line("T0008 OK moved");

        var results = await Actions(connection).MoveAsync([Message("7"), Message("8"), Message("9")], "Archive");

        Assert.Equal([MailActionOutcome.Done, MailActionOutcome.Failed, MailActionOutcome.Done], results.Select(r => r.Outcome));
        Assert.Equal(MailActionCodes.ServerRefused, results[1].Code);
        Assert.Contains("locked", results[1].Reason, StringComparison.Ordinal);
    }

    /// <summary>A UID the folder no longer holds fails with MessageNotFound and nothing is sent for it.</summary>
    [Fact(DisplayName = "REQ-RAG-110 MissingMessageFailsAlone")]
    public async Task MissingMessageFailsAlone()
    {
        var connection = Session("IMAP4rev1 MOVE", ExchangeFolders, "INBOX", "7 9")
            .Line("T0006 OK moved")
            .Line("T0007 OK moved");

        var results = await Actions(connection).MoveAsync([Message("7"), Message("8"), Message("9")], "Archive");

        Assert.Equal(MailActionCodes.MessageNotFound, results[1].Code);
        Assert.DoesNotContain(connection.Written, line => line.Contains("MOVE 8", StringComparison.Ordinal));
        Assert.Equal(MailActionOutcome.Done, results[2].Outcome);
    }

    /// <summary>A changed UIDVALIDITY means the UID may name a different message, so nothing is done to it.</summary>
    [Fact(DisplayName = "REQ-RAG-110 UidValidityChangeRefusesTheAction")]
    public async Task UidValidityChangeRefusesTheAction()
    {
        var connection = Session("IMAP4rev1 MOVE", ExchangeFolders, "INBOX", "7");

        var result = (await Actions(connection).TrashAsync([new MailMessageRef("INBOX", "7", "10")])).Single();

        Assert.Equal(MailActionCodes.UidValidityChanged, result.Code);
        Assert.DoesNotContain(connection.Written, line => line.Contains("SEARCH", StringComparison.Ordinal));
        AssertNoMutatingCommand(connection);
    }

    /// <summary>A destination folder that does not exist is refused with a code; folders are never created.</summary>
    [Fact(DisplayName = "REQ-RAG-110 MoveToMissingFolderRefused")]
    public async Task MoveToMissingFolderRefused()
    {
        var connection = Session("IMAP4rev1 MOVE", ExchangeFolders, "INBOX", "7");

        var result = (await Actions(connection).MoveAsync([Message("7")], "Nowhere")).Single();

        Assert.Equal(MailActionCodes.TargetFolderNotFound, result.Code);
        AssertNoMutatingCommand(connection);
    }

    /// <summary>
    /// A folder name or label carrying a line break is refused per message with InvalidArgument; the
    /// injected command never reaches the wire, and every line written is one command.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-110 InjectedFolderOrLabelRefused")]
    public async Task InjectedFolderOrLabelRefused()
    {
        var connection = new ScriptedImapConnection();

        var results = await Actions(connection).ApplyAsync(
        [
            MailAction.Move(Message("7"), "Archive" + Payload),
            MailAction.AddLabel(Message("8"), "Receipts" + Payload),
            MailAction.Trash(new MailMessageRef("INBOX" + Payload, "9")),
            MailAction.Trash(new MailMessageRef("INBOX", "9 +FLAGS")),
        ]);

        Assert.All(results, result => Assert.Equal(MailActionCodes.InvalidArgument, result.Code));
        Assert.Empty(connection.Written);
    }

    /// <summary>The session the reader uses refuses any mutating verb before it is written, so the transport stays read-only by construction.</summary>
    [Fact(DisplayName = "REQ-RAG-110 ReadOnlySessionRefusesMutatingCommands")]
    public async Task ReadOnlySessionRefusesMutatingCommands()
    {
        var connection = new ScriptedImapConnection().Line("* OK ready").Line("T0001 OK logged in");
        using var session = new ImapSession(() => connection, Options(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, readOnly: true);
        await session.EnsureConnectedAsync(CancellationToken.None);

        foreach (var command in new[] { "UID MOVE 7 \"Archive\"", "UID STORE 7 +FLAGS (\\Seen)", "UID COPY 7 \"A\"", "UID EXPUNGE 7", "UID FETCH 7 (BODY[])" })
        {
            await Assert.ThrowsAsync<ConnectorException>(() => session.RunAsync(command, CancellationToken.None));
        }

        Assert.Single(connection.Written);
    }

    /// <summary>Even a writable session never sends a bare EXPUNGE or CLOSE, both of which purge every \Deleted message.</summary>
    [Fact(DisplayName = "REQ-RAG-110 WritableSessionNeverSendsBareExpunge")]
    public async Task WritableSessionNeverSendsBareExpunge()
    {
        var connection = new ScriptedImapConnection().Line("* OK ready").Line("T0001 OK logged in");
        using var session = new ImapSession(() => connection, Options(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, readOnly: false);
        await session.EnsureConnectedAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ConnectorException>(() => session.RunAsync("EXPUNGE", CancellationToken.None));
        await Assert.ThrowsAsync<ConnectorException>(() => session.RunAsync("CLOSE", CancellationToken.None));
        await Assert.ThrowsAsync<ConnectorException>(() => session.RunAsync("UID EXPUNGE", CancellationToken.None));

        Assert.Single(connection.Written);
    }

    /// <summary>An email connector item id converts to a reference, folder slashes included.</summary>
    [Fact(DisplayName = "REQ-RAG-110 ReferenceFromConnectorItemId")]
    public void ReferenceFromConnectorItemId()
    {
        Assert.Equal(new MailMessageRef("[Gmail]/All Mail", "42", "7"), MailMessageRef.FromConnectorItemId("[Gmail]/All Mail/7/42"));
        Assert.Throws<ArgumentException>(() => MailMessageRef.FromConnectorItemId("inbox.mbox/abc"));
    }

    /// <summary>
    /// Acceptance of REQ-RAG-111: the same three actions dry-run on Gmail come back as one plan per
    /// message with the commands that would be sent, and the wire log holds only read commands —
    /// the folder is opened with EXAMINE, and no MOVE, STORE, COPY or EXPUNGE is written.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-111 DryRunSendsNoMutatingCommand")]
    public async Task DryRunSendsNoMutatingCommand()
    {
        var connection = Session(Gmail, GmailFolders, "INBOX", "7 8 9", examine: true);

        var plans = await Actions(connection).DryRunAsync(ThreeActions());

        Assert.Equal(3, plans.Count);
        Assert.All(plans, plan => Assert.Equal(MailActionOutcome.Done, plan.Outcome));
        Assert.Equal(["UID MOVE 7 \"Archive\""], plans[0].Commands);
        Assert.Equal(["UID STORE 8 +X-GM-LABELS (\"Receipts\")"], plans[1].Commands);
        Assert.Equal("[Gmail]/Trash", plans[2].TargetFolder);
        Assert.Equal(MailMoveStrategy.Move, plans[2].Strategy);
        Assert.Equal(["LOGIN", "CAPABILITY", "LIST", "EXAMINE", "UID SEARCH"], connection.Written.Select(Verb));
        AssertNoMutatingCommand(connection);
    }

    /// <summary>
    /// A dry run on a server with UIDPLUS but no MOVE reports the copy-and-expunge strategy, the
    /// resolved \Trash folder and each refusal per message, still sending nothing that changes the mailbox.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-111 DryRunReportsStrategyTrashAndRefusals")]
    public async Task DryRunReportsStrategyTrashAndRefusals()
    {
        var connection = Session("IMAP4rev1 UIDPLUS", ExchangeFolders, "INBOX", "7 8 9", examine: true);

        var plans = await Actions(connection).DryRunAsync(ThreeActions());

        Assert.Equal(MailMoveStrategy.CopyAndExpunge, plans[0].Strategy);
        Assert.Equal(["UID COPY 7 \"Archive\"", "UID STORE 7 +FLAGS.SILENT (\\Deleted)", "UID EXPUNGE 7"], plans[0].Commands);
        Assert.Equal(MailActionCodes.LabelsNotSupported, plans[1].Code);
        Assert.Empty(plans[1].Commands);
        Assert.Equal("Deleted Items", plans[2].TargetFolder);
        AssertNoMutatingCommand(connection);
    }

    private static IReadOnlyList<MailAction> ThreeActions() =>
    [
        MailAction.Move(Message("7"), "Archive"),
        MailAction.AddLabel(Message("8"), "Receipts"),
        MailAction.Trash(Message("9")),
    ];

    private static MailMessageRef Message(string uid) => new("INBOX", uid, "11");

    private static ScriptedImapConnection Session(
        string capabilities,
        IEnumerable<string> folders,
        string folder,
        string present,
        bool examine = false)
    {
        var connection = new ScriptedImapConnection()
            .Line("* OK ready")
            .Line("T0001 OK logged in")
            .Line("* CAPABILITY " + capabilities)
            .Line("T0002 OK done");

        foreach (var line in folders)
        {
            connection.Line(line);
        }

        return connection
            .Line("T0003 OK listed")
            .Line("* OK [UIDVALIDITY 11] valid")
            .Line(examine ? "T0004 OK [READ-ONLY] examined" : $"T0004 OK [READ-WRITE] {folder} selected")
            .Line("* SEARCH " + present)
            .Line("T0005 OK searched");
    }

    private static ImapMailboxOptions Options() =>
        new() { Host = "imap.example.test", Username = "ada@example.test", Password = "hunter2" };

    private static ImapMailActions Actions(IImapConnection connection) => new(() => connection, Options());

    private static string Untagged(string line)
    {
        var space = line.IndexOf(' ');
        return space >= 0 ? line[(space + 1)..] : line;
    }

    private static string Verb(string line)
    {
        var parts = Untagged(line).Split(' ');
        return parts[0] == "UID" ? $"UID {parts[1]}" : parts[0];
    }

    private static void AssertNoMutatingCommand(ScriptedImapConnection connection)
    {
        string[] mutating = ["MOVE", "COPY", "STORE", "EXPUNGE", "APPEND", "DELETE", "CLOSE", "CREATE", "RENAME"];
        foreach (var line in connection.Written)
        {
            var verb = Verb(line).Replace("UID ", string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(verb, mutating);
            Assert.DoesNotContain('\n', line);
            Assert.DoesNotContain('\r', line);
        }
    }
}
