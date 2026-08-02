using TechieDesk.Services.Connectors;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Connectors;

/// <summary>
/// Tests for the mailbox connector's settings, validation and option mapping (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// The scope filters and the TLS refusal are the requirement, not polish — BRD-135 calls this the
/// highest-sensitivity source in the product — so they are asserted here rather than left to a live
/// mailbox nobody has on a dev machine.
/// </remarks>
public class EmailConnectorSettingsTests
{
    /// <summary>Builds settings for a plausible IMAP mailbox, so each test varies one thing.</summary>
    private static ConnectorSettings Imap() => new()
    {
        ImapHost = "imap.example.com",
        ImapUsername = "person@example.com",
    };

    /// <summary>Verifies the mailbox type is offered as something this build can add.</summary>
    [Fact]
    public void EmailIsAKnownConnectorType()
    {
        Assert.True(ConnectorTypes.IsKnown(ConnectorTypes.Email));
        Assert.Contains(ConnectorTypes.All, type => type.ConnectorType == ConnectorTypes.Email);
    }

    /// <summary>Verifies a mailbox naming neither a server nor a file is refused.</summary>
    [Fact]
    public void AMailboxWithNoServerAndNoFileIsRefused()
    {
        var result = new ConnectorSettings().Validate(ConnectorTypes.Email);

        Assert.NotNull(result);
        Assert.Contains("IMAP server", result, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies naming both a server and an archive is refused rather than silently merged.</summary>
    [Fact]
    public void AMailboxNamingBothAServerAndAnArchiveIsRefused()
    {
        var settings = Imap() with { MboxPath = "/Users/person/mail.mbox" };

        Assert.NotNull(settings.Validate(ConnectorTypes.Email));
    }

    /// <summary>Verifies an IMAP mailbox with no account name is refused.</summary>
    [Fact]
    public void AnImapMailboxWithNoAccountNameIsRefused()
    {
        var settings = Imap() with { ImapUsername = null };

        Assert.NotNull(settings.Validate(ConnectorTypes.Email));
    }

    /// <summary>
    /// Verifies the cleartext mail ports are refused, which is the security half of BRD-135.
    /// </summary>
    /// <param name="port">A port that implies an unencrypted session.</param>
    /// <remarks>
    /// Refused at SAVE time on purpose. The library only speaks implicit TLS, so a cleartext port
    /// cannot work at all — failing here names the reason, where failing at run time is a socket
    /// error on a schedule at 07:00.
    /// </remarks>
    [Theory]
    [InlineData(143)]
    [InlineData(110)]
    public void CleartextMailPortsAreRefused(int port)
    {
        var result = (Imap() with { ImapPort = port }).Validate(ConnectorTypes.Email);

        Assert.NotNull(result);
        Assert.Contains("cleartext", result, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies the cleartext IMAP scheme is refused if typed into the host box.</summary>
    [Fact]
    public void TheCleartextImapSchemeIsRefused()
    {
        var result = (Imap() with { ImapHost = "imap://mail.example.com" }).Validate(ConnectorTypes.Email);

        Assert.NotNull(result);
    }

    /// <summary>Verifies a TLS mailbox on the default port is accepted.</summary>
    [Fact]
    public void AnImapMailboxOnTheImplicitTlsPortIsAccepted()
    {
        Assert.Null(Imap().Validate(ConnectorTypes.Email));
        Assert.Null((Imap() with { ImapPort = 993 }).Validate(ConnectorTypes.Email));
    }

    /// <summary>Verifies a relative archive path is refused and a rooted one accepted.</summary>
    [Fact]
    public void AnMboxArchiveNeedsAFullPath()
    {
        Assert.NotNull(new ConnectorSettings { MboxPath = "mail.mbox" }.Validate(ConnectorTypes.Email));
        Assert.Null(
            new ConnectorSettings { MboxPath = "/Users/person/mail.mbox" }.Validate(ConnectorTypes.Email));
    }

    /// <summary>Verifies the archive shape is recognised only when there is no server.</summary>
    [Fact]
    public void TheArchiveShapeIsRecognisedOnlyWithoutAServer()
    {
        Assert.True(new ConnectorSettings { MboxPath = "/a/b.mbox" }.IsMboxMailbox);
        Assert.False(Imap().IsMboxMailbox);
    }

    /// <summary>
    /// Verifies the narrow library defaults survive an empty form — the heart of BRD-135's scoping.
    /// </summary>
    /// <remarks>
    /// A blank folder list must mean INBOX alone, never "every folder", and sent/spam/attachments must
    /// stay off. Widening has to be a deliberate act, so this asserts that not-typing-anything cannot
    /// widen it.
    /// </remarks>
    [Fact]
    public void AnEmptyFormKeepsTheNarrowDefaults()
    {
        var options = Imap().ToEmailOptions();

        Assert.Equal(["INBOX"], options.Folders);
        Assert.False(options.IncludeSentByMe);
        Assert.False(options.IncludeSpam);
        Assert.False(options.IncludeAttachments);
        Assert.Null(options.SinceUtc);
        Assert.Null(options.SenderContains);
        Assert.Null(options.SubjectContains);
    }

    /// <summary>Verifies reply and signature stripping default to on, as BRD-135 requires.</summary>
    [Fact]
    public void QuotedRepliesAndSignaturesAreStrippedByDefault()
    {
        var options = Imap().ToEmailOptions();

        Assert.True(options.StripQuotedReplies);
        Assert.True(options.StripSignatures);
    }

    /// <summary>Verifies the scope filters reach the library options.</summary>
    [Fact]
    public void ScopeFiltersReachTheLibraryOptions()
    {
        var since = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var settings = Imap() with
        {
            MailFolders = ["INBOX", "Projects"],
            MailSinceUtc = since,
            MailSenderContains = " @acme.com ",
            MailSubjectContains = " invoice ",
            MailIncludeSentByMe = true,
            MailIncludeAttachments = true,
            MailMaxAttachmentBytes = 1024,
        };

        var options = settings.ToEmailOptions();

        Assert.Equal(["INBOX", "Projects"], options.Folders);
        Assert.Equal(since, options.SinceUtc);
        Assert.Equal("@acme.com", options.SenderContains);
        Assert.Equal("invoice", options.SubjectContains);
        Assert.True(options.IncludeSentByMe);
        Assert.Equal(1024, options.MaxAttachmentBytes);
    }

    /// <summary>Verifies the credential is placed on the mailbox options and the port defaulted.</summary>
    [Fact]
    public void TheCredentialAndPortReachTheMailboxOptions()
    {
        var mailbox = Imap().ToImapOptions("hunter2");

        Assert.Equal("imap.example.com", mailbox.Host);
        Assert.Equal(ConnectorSettings.DefaultImapPort, mailbox.Port);
        Assert.Equal("person@example.com", mailbox.Username);
        Assert.Equal("hunter2", mailbox.Password);
        Assert.False(mailbox.UseOAuthBearer);
    }

    /// <summary>Verifies the OAuth flag rides through to the transport, for Gmail and Microsoft 365.</summary>
    [Fact]
    public void TheOAuthFlagReachesTheMailboxOptions()
    {
        var mailbox = (Imap() with { ImapUseOAuthBearer = true }).ToImapOptions("ya29.token");

        Assert.True(mailbox.UseOAuthBearer);
        Assert.Equal("ya29.token", mailbox.Password);
    }

    /// <summary>Verifies a missing credential becomes empty rather than null on the options object.</summary>
    [Fact]
    public void AMissingCredentialDoesNotBecomeANullPassword()
    {
        Assert.Equal(string.Empty, Imap().ToImapOptions(null).Password);
    }

    /// <summary>Verifies the settings survive the JSON round trip they are stored through.</summary>
    [Fact]
    public void MailboxSettingsSurviveTheJsonRoundTrip()
    {
        var original = Imap() with
        {
            ImapPort = 993,
            ImapUseOAuthBearer = true,
            MailFolders = ["INBOX", "Archive"],
            MailSenderContains = "acme",
            MailIncludeSpam = true,
            MailStripSignatures = false,
        };

        var restored = ConnectorSettings.Parse(original.ToJson());

        Assert.Equal("imap.example.com", restored.ImapHost);
        Assert.Equal(993, restored.ImapPort);
        Assert.True(restored.ImapUseOAuthBearer);
        Assert.Equal(["INBOX", "Archive"], restored.MailFolders);
        Assert.Equal("acme", restored.MailSenderContains);
        Assert.True(restored.MailIncludeSpam);
        Assert.False(restored.MailStripSignatures);
    }

    /// <summary>Verifies the one-line summary names the mailbox without leaking a secret.</summary>
    [Fact]
    public void TheSummaryNamesTheMailboxAndNeverACredential()
    {
        using var resources = new ResourceHarness("en");
        var summary = (Imap() with { MailFolders = ["INBOX", "Projects"] })
            .Describe(ConnectorTypes.Email, resources.Localize);

        Assert.Contains("imap.example.com", summary, StringComparison.Ordinal);
        Assert.Contains("INBOX", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", summary, StringComparison.Ordinal);
    }

    /// <summary>Verifies an archive summary names the file rather than a server.</summary>
    [Fact]
    public void TheSummaryOfAnArchiveNamesTheFile()
    {
        using var resources = new ResourceHarness("en");
        var summary = new ConnectorSettings { MboxPath = "/Users/person/2025.mbox" }
            .Describe(ConnectorTypes.Email, resources.Localize);

        Assert.Contains("2025.mbox", summary, StringComparison.Ordinal);
    }
}
