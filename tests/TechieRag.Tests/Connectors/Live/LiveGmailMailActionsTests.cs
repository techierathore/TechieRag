using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors.Live;

/// <summary>
/// REQ-RAG-110 / BRD-169 and REQ-RAG-111 / BRD-170 against a real Gmail mailbox.
/// </summary>
/// <remarks>
/// What the scripted suite cannot prove: that Gmail really advertises <c>MOVE</c> and
/// <c>X-GM-EXT-1</c>, really lists <c>[Gmail]/Trash</c> with <c>\Trash</c>, and really accepts the
/// exact <c>UID MOVE</c> and <c>X-GM-LABELS</c> commands this library composes. Every change is put
/// back before the test ends: the label is removed, and the archived and trashed messages are moved
/// back to INBOX (found again by Message-ID).
/// </remarks>
[Trait("Category", LiveGmailFactAttribute.CategoryName)]
public sealed class LiveGmailMailActionsTests
{
    private const string AllMail = "[Gmail]/All Mail";
    private const string TestLabel = "TechieRagLiveTest";

    /// <summary>
    /// Acceptance of REQ-RAG-111 then REQ-RAG-110 on Gmail: a dry run of move, label and Trash on three
    /// INBOX messages plans each and leaves INBOX unchanged; applying them puts each message where it
    /// was sent with its own Done result; the test then restores the mailbox.
    /// </summary>
    [LiveGmailFact(DisplayName = "REQ-RAG-110 LiveGmailMoveLabelTrashAndDryRun")]
    public async Task LiveGmailMoveLabelTrashAndDryRun()
    {
        var options = new ImapMailboxOptions { Host = "imap.gmail.com", Username = LiveGmailFactAttribute.User, Password = LiveGmailFactAttribute.AppPassword };
        var before = await InboxAsync(options);
        Assert.True(before.Count >= 3, "The Gmail test account needs at least three messages in INBOX.");

        var picked = before.TakeLast(3).ToList();
        var refs = picked.Select(MailMessageRef.FromHeader).ToList();
        MailAction[] actions = [MailAction.Move(refs[0], AllMail), MailAction.AddLabel(refs[1], TestLabel), MailAction.Trash(refs[2])];
        var mailActions = ImapMailActions.Create(options);

        var plans = await mailActions.DryRunAsync(actions);

        Assert.All(plans, plan => Assert.Equal(MailActionOutcome.Done, plan.Outcome));
        Assert.Equal("[Gmail]/Trash", plans[2].TargetFolder);
        Assert.Equal(before.Select(h => h.Uid), (await InboxAsync(options)).Select(h => h.Uid));

        var results = await mailActions.ApplyAsync(actions);

        try
        {
            Assert.All(results, result => Assert.Equal(MailActionOutcome.Done, result.Outcome));
            var inboxUids = (await InboxAsync(options)).Select(h => h.Uid).ToHashSet();
            Assert.DoesNotContain(picked[0].Uid, inboxUids);
            Assert.Contains(picked[1].Uid, inboxUids);
            Assert.DoesNotContain(picked[2].Uid, inboxUids);
        }
        finally
        {
            await mailActions.RemoveLabelAsync([refs[1]], TestLabel);
            await RestoreAsync(options, mailActions, AllMail, picked[0]);
            await RestoreAsync(options, mailActions, "[Gmail]/Trash", picked[2]);
        }
    }

    private static async Task<List<MailHeader>> InboxAsync(ImapMailboxOptions options)
    {
        using var transport = ImapMailTransport.Create(options);
        var page = await transport.SearchAsync("INBOX", new MailSearchCriteria(), 0, 500);
        return [.. page.Headers];
    }

    private static async Task RestoreAsync(ImapMailboxOptions options, ImapMailActions mailActions, string folder, MailHeader original)
    {
        using var transport = ImapMailTransport.Create(options);
        var subject = original.Subject.Length > 40 ? original.Subject[..40] : original.Subject;
        var page = await transport.SearchAsync(folder, new MailSearchCriteria(null, null, subject.Length > 0 ? subject : null), 0, 200);
        var moved = page.Headers.FirstOrDefault(h => h.MessageId is not null && h.MessageId == original.MessageId);
        if (moved is not null)
        {
            await mailActions.MoveAsync([MailMessageRef.FromHeader(moved)], "INBOX");
        }
    }
}
