using System.Globalization;
using System.Text.Json;
using TechieDesk.Services.Localization;
using TechieRag.Connectors.Confluence;
using TechieRag.Connectors.Email;
using TechieRag.Connectors.Repository;
using TechieRag.Web;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// The non-secret configuration of one saved connector, as stored in the <c>Connector</c> table's
/// <c>Settings</c> column (REQ-RAG-019, REQ-RAG-020).
/// </summary>
/// <remarks>
/// <para><b>One record for every connector type, on purpose.</b> The alternative — a settings class
/// per type with a discriminator — buys type safety at the cost of a polymorphic JSON converter, a
/// second place where a type key is written down, and a migration every time a connector gains a
/// field. The fields here are grouped by the type that uses them and validated per type by
/// <see cref="Validate"/>, which is where the real safety is: an empty <see cref="ProjectPath"/> is
/// refused at save time, not at 07:00 three days later.</para>
/// <para><b>No credential lives here.</b> This record is serialized into the application database in
/// cleartext, so a token field on it would be a token in the database — exactly what REQ-FN-039
/// forbids. <see cref="UserEmail"/> is the one identity-shaped field present, and it is an account
/// name, not a secret: Confluence Cloud pairs it with a token that lives in the OS credential store.
/// </para>
/// <para><b><see cref="AllowPrivateNetwork"/> is an explicit operator decision, never a default.</b>
/// The library's transport refuses loopback, private and link-local targets by default, because a
/// connector base URL arrives from whoever can configure the workspace and every request carries the
/// source's credential in an <c>Authorization</c> header — a URL aimed at an internal endpoint does
/// not merely read it, it hands the token to it. A self-hosted GitLab or Confluence on the company
/// LAN is a real and normal case, so it is offered — as a switch the person who meant it turns on,
/// and one that is carried to <i>both</i> call sites the library requires it at.</para>
/// </remarks>
public sealed record ConnectorSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Gets the repository host API to speak — <c>GitHub</c> or <c>GitLab</c>.</summary>
    /// <remarks>Repository connectors only. Absent or unreadable means GitHub, the library's own default.</remarks>
    public string? Host { get; init; }

    /// <summary>Gets the project as <c>owner/repository</c>, or <c>group/subgroup/project</c>.</summary>
    /// <remarks>Repository connectors only. Required.</remarks>
    public string? ProjectPath { get; init; }

    /// <summary>Gets the branch, tag or commit to read, or <see langword="null"/> for the default branch.</summary>
    /// <remarks>
    /// Null is a real answer, not a missing one: the connector asks the host what the project's
    /// default branch is. Substituting "main" here would report every repository still on "master"
    /// as empty rather than as misconfigured.
    /// </remarks>
    public string? Branch { get; init; }

    /// <summary>Gets the API base URL override, for GitHub Enterprise Server or self-managed GitLab.</summary>
    public string? ApiBaseUrl { get; init; }

    /// <summary>Gets the web base URL override, used to build the links a citation points at.</summary>
    public string? WebBaseUrl { get; init; }

    /// <summary>Gets the glob patterns a file path must match to be ingested.</summary>
    /// <remarks>
    /// Empty includes everything, which is almost never meant: a repository is mostly lockfiles,
    /// fixtures and build output, and ingesting all of it buries the prose the user is searching for.
    /// </remarks>
    public IReadOnlyList<string> IncludeGlobs { get; init; } = [];

    /// <summary>Gets the glob patterns that exclude a file path outright.</summary>
    /// <remarks>Applied after <see cref="IncludeGlobs"/>; an exclude always wins.</remarks>
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = [];

    /// <summary>Gets the Confluence site base URL, without a trailing slash.</summary>
    /// <remarks>Confluence connectors only. Cloud sites include the <c>/wiki</c> suffix.</remarks>
    public string? BaseUrl { get; init; }

    /// <summary>Gets the Confluence space key to ingest.</summary>
    /// <remarks>Mutually exclusive with <see cref="RootPageId"/>.</remarks>
    public string? SpaceKey { get; init; }

    /// <summary>Gets the id of the Confluence page whose tree to ingest.</summary>
    /// <remarks>Mutually exclusive with <see cref="SpaceKey"/>.</remarks>
    public string? RootPageId { get; init; }

    /// <summary>Gets a value indicating whether a page-tree walk descends past the root page.</summary>
    public bool IncludeChildPages { get; init; } = true;

    /// <summary>Gets the Atlassian Cloud account email paired with the API token.</summary>
    /// <remarks>Left empty on Server / Data Center, where the token alone is sent as a bearer token.</remarks>
    public string? UserEmail { get; init; }

    /// <summary>
    /// Gets a value indicating whether this connector may reach a loopback, private or link-local
    /// address.
    /// </summary>
    /// <remarks>See the remarks on this record: an opt-in, never a default, and never inferred.</remarks>
    public bool AllowPrivateNetwork { get; init; }

    /// <summary>Gets how many entries to request per listing page, or zero for the library default.</summary>
    public int PageSize { get; init; }

    /// <summary>Gets the IMAP host, or empty when this mailbox is a local <c>.mbox</c> file.</summary>
    /// <remarks>REQ-RAG-049 / BRD-135. Blank plus <see cref="MboxPath"/> selects the file transport.</remarks>
    public string? ImapHost { get; init; }

    /// <summary>Gets the IMAP port. Zero means the implicit-TLS default, 993.</summary>
    public int ImapPort { get; init; }

    /// <summary>Gets the IMAP account name (usually the address).</summary>
    public string? ImapUsername { get; init; }

    /// <summary>Gets a value indicating whether the stored secret is an OAuth bearer token.</summary>
    /// <remarks>
    /// Gmail and Microsoft 365 both retired password sign-in for IMAP; the credential is an access
    /// token sent via SASL XOAUTH2. The store holds a token either way — only the framing differs.
    /// </remarks>
    public bool ImapUseOAuthBearer { get; init; }

    /// <summary>Gets the path to a local <c>.mbox</c> archive, or empty when reading IMAP.</summary>
    public string? MboxPath { get; init; }

    /// <summary>Gets the folders to read. Empty means <c>INBOX</c> alone.</summary>
    public IReadOnlyList<string> MailFolders { get; init; } = [];

    /// <summary>Gets the earliest message date to read, or <see langword="null"/> for no floor.</summary>
    public DateTimeOffset? MailSinceUtc { get; init; }

    /// <summary>Gets the substring a sender must contain, or <see langword="null"/> for any sender.</summary>
    public string? MailSenderContains { get; init; }

    /// <summary>Gets the substring a subject must contain, or <see langword="null"/> for any subject.</summary>
    public string? MailSubjectContains { get; init; }

    /// <summary>Gets the mailbox owner's own address, used to recognise mail they sent.</summary>
    public string? MailAccountAddress { get; init; }

    /// <summary>Gets a value indicating whether mail sent by the account owner is ingested.</summary>
    public bool MailIncludeSentByMe { get; init; }

    /// <summary>Gets a value indicating whether spam and junk folders are ingested.</summary>
    /// <remarks>Off by default, and deliberately a separate switch from the folder list.</remarks>
    public bool MailIncludeSpam { get; init; }

    /// <summary>Gets a value indicating whether attachments are ingested alongside message bodies.</summary>
    public bool MailIncludeAttachments { get; init; }

    /// <summary>Gets the attachment extensions to read. Empty means the library's default set.</summary>
    public IReadOnlyList<string> MailAttachmentExtensions { get; init; } = [];

    /// <summary>Gets the per-attachment size ceiling in bytes, or zero for the library default.</summary>
    public long MailMaxAttachmentBytes { get; init; }

    /// <summary>Gets a value indicating whether quoted reply chains are stripped before ingestion.</summary>
    public bool MailStripQuotedReplies { get; init; } = true;

    /// <summary>Gets a value indicating whether signature blocks are stripped before ingestion.</summary>
    public bool MailStripSignatures { get; init; } = true;

    /// <summary>Reads settings from their stored JSON form.</summary>
    /// <param name="json">The stored settings, or <see langword="null"/>.</param>
    /// <returns>The settings, or an empty instance when the string is absent or not readable.</returns>
    /// <remarks>
    /// Never throws. A row hand-edited or written by an older build must surface through
    /// <see cref="Validate"/> as "this connector's configuration is not usable", which is a named run
    /// failure — not an exception escaping onto a background timer.
    /// </remarks>
    public static ConnectorSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ConnectorSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<ConnectorSettings>(json, SerializerOptions)
                ?? new ConnectorSettings();
        }
        catch (JsonException)
        {
            return new ConnectorSettings();
        }
    }

    /// <summary>Writes the settings to the string form stored in the <c>Settings</c> column.</summary>
    /// <returns>The JSON settings.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>Checks these settings against the connector type that will consume them.</summary>
    /// <param name="connectorType">The connector type key.</param>
    /// <returns><see langword="null"/> when the settings are usable, otherwise the reason they are not.</returns>
    public string? Validate(string? connectorType) => connectorType switch
    {
        ConnectorTypes.Repository => ValidateRepository(),
        ConnectorTypes.Confluence => ValidateConfluence(),
        ConnectorTypes.Email => ValidateEmail(),
        _ => $"'{connectorType}' is not a kind of connector this build can run.",
    };

    /// <summary>The IMAP folder read when the operator named none. Wire vocabulary, not a label.</summary>
    /// <remarks>
    /// REQ-UI-051: <c>INBOX</c> is the folder name sent to the IMAP server in a <c>SELECT</c>, so it
    /// is culture-invariant and is rendered as itself inside a Devanagari sentence. Translating it
    /// would produce a mailbox no server has.
    /// </remarks>
    public const string DefaultMailFolder = "INBOX";

    /// <summary>Renders the settings as the one-line summary shown against the saved connector.</summary>
    /// <param name="connectorType">The connector type key.</param>
    /// <param name="localize">Resolves the resource keys this summary is assembled from.</param>
    /// <returns>A plain-language summary. Never a credential, never JSON.</returns>
    /// <remarks>
    /// <para><b>REQ-UI-051 / BRD-91.</b> This summary is the second line under every saved connector
    /// on the hub, and it used to be built from English literals in this record — invisible to both
    /// razor counters, because it is composed in a service. It is one of the few places
    /// <see cref="LocalizeText"/> is the right answer rather than a bare resource key: the sentence
    /// is assembled here from up to four optional fragments and from user data, and handing the hub
    /// a key plus a bag of arguments would just move that assembly into the page.</para>
    /// <para>Everything substituted IN is invariant — a repository host brand, an <c>owner/repo</c>
    /// path, a branch name, a site URL, a mailbox address, an IMAP folder name. Only the connecting
    /// words are translated, which is the split that keeps a Hindi summary pointing at the same
    /// source as an English one.</para>
    /// <para><b>The <c>·</c> and <c>@</c> separators stay in code and are NOT resources.</b> A
    /// resource whose whole value is punctuation carries no Devanagari and is byte-identical to its
    /// English source, which is precisely what
    /// <c>LocalizationTests.EveryHindiStringIsWrittenInDevanagari</c> and
    /// <c>TranslationsAreNotCopiesOfTheEnglish</c> exist to reject — and they are right to: a
    /// translator handed "{0} · {1}" has nothing to translate and no way to tell it was a mistake.
    /// Every WORD in this summary is a key; only the glue between invariant identifiers is not.</para>
    /// </remarks>
    public string Describe(string? connectorType, LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        return connectorType switch
        {
            ConnectorTypes.Repository =>
                $"{ResolveHost()} · {ProjectPath} @ "
                + (string.IsNullOrWhiteSpace(Branch) ? localize("ConnectorDescribeDefaultBranch") : Branch),
            ConnectorTypes.Confluence => string.IsNullOrWhiteSpace(SpaceKey)
                ? localize(
                    IncludeChildPages
                        ? "ConnectorDescribeConfluencePageTree"
                        : "ConnectorDescribeConfluencePage",
                    BaseUrl ?? string.Empty,
                    RootPageId ?? string.Empty)
                : localize("ConnectorDescribeConfluenceSpace", BaseUrl ?? string.Empty, SpaceKey),
            ConnectorTypes.Email => DescribeEmail(localize),
            _ => localize("ConnectorDescribeUnknown"),
        };
    }

    /// <summary>Gets the repository host these settings name.</summary>
    /// <returns>The parsed host, defaulting to <see cref="RepositoryHost.GitHub"/>.</returns>
    public RepositoryHost ResolveHost() =>
        Enum.TryParse<RepositoryHost>(Host, ignoreCase: true, out var parsed)
            ? parsed
            : RepositoryHost.GitHub;

    /// <summary>Builds the library options for a repository connector.</summary>
    /// <param name="accessToken">The token resolved from the OS credential store, or <see langword="null"/> to read anonymously.</param>
    /// <returns>The options object, holding the credential for the life of the run only.</returns>
    public RepositoryConnectorOptions ToRepositoryOptions(string? accessToken)
    {
        var options = new RepositoryConnectorOptions
        {
            Host = ResolveHost(),
            ProjectPath = ProjectPath ?? string.Empty,
            Branch = string.IsNullOrWhiteSpace(Branch) ? null : Branch.Trim(),
            ApiBaseUrl = Blank(ApiBaseUrl),
            WebBaseUrl = Blank(WebBaseUrl),
            AccessToken = Blank(accessToken),
            IncludeGlobs = [.. IncludeGlobs],
            ExcludeGlobs = [.. ExcludeGlobs],
        };

        if (PageSize > 0)
        {
            options.PageSize = PageSize;
        }

        return options;
    }

    /// <summary>Builds the library options for a Confluence connector.</summary>
    /// <param name="apiToken">The token resolved from the OS credential store, or <see langword="null"/> to read anonymously.</param>
    /// <returns>The options object, holding the credential for the life of the run only.</returns>
    public ConfluenceConnectorOptions ToConfluenceOptions(string? apiToken)
    {
        var options = new ConfluenceConnectorOptions
        {
            BaseUrl = BaseUrl ?? string.Empty,
            SpaceKey = Blank(SpaceKey),
            RootPageId = Blank(RootPageId),
            IncludeChildPages = IncludeChildPages,
            UserEmail = Blank(UserEmail),
            ApiToken = Blank(apiToken),
        };

        if (PageSize > 0)
        {
            options.PageSize = PageSize;
        }

        return options;
    }

    /// <summary>Gets the default IMAP port when none is configured.</summary>
    /// <remarks>993 is implicit TLS. There is deliberately no 143 path — see <see cref="ValidateEmail"/>.</remarks>
    public const int DefaultImapPort = 993;

    /// <summary>Gets a value indicating whether these settings name a local archive rather than a server.</summary>
    public bool IsMboxMailbox =>
        string.IsNullOrWhiteSpace(ImapHost) && !string.IsNullOrWhiteSpace(MboxPath);

    /// <summary>Builds the library options describing WHAT to ingest from a mailbox.</summary>
    /// <returns>The scope options. Holds no credential.</returns>
    /// <remarks>
    /// The library's own defaults are the narrow ones and are preserved here: an empty folder list
    /// means <c>INBOX</c> alone, not "every folder", and spam/sent are opt-in. Only values the
    /// operator actually set are copied over, so a zero or empty field never silently widens scope.
    /// </remarks>
    public EmailConnectorOptions ToEmailOptions()
    {
        var options = new EmailConnectorOptions
        {
            SinceUtc = MailSinceUtc,
            SenderContains = Blank(MailSenderContains),
            SubjectContains = Blank(MailSubjectContains),
            AccountAddress = Blank(MailAccountAddress),
            IncludeSentByMe = MailIncludeSentByMe,
            IncludeSpam = MailIncludeSpam,
            IncludeAttachments = MailIncludeAttachments,
            StripQuotedReplies = MailStripQuotedReplies,
            StripSignatures = MailStripSignatures,
        };

        if (MailFolders.Count > 0)
        {
            options.Folders = [.. MailFolders];
        }

        if (MailIncludeAttachments && MailAttachmentExtensions.Count > 0)
        {
            options.AttachmentExtensions = [.. MailAttachmentExtensions];
        }

        if (MailMaxAttachmentBytes > 0)
        {
            options.MaxAttachmentBytes = MailMaxAttachmentBytes;
        }

        if (PageSize > 0)
        {
            options.PageSize = PageSize;
        }

        return options;
    }

    /// <summary>Builds the library options describing HOW to reach the mailbox.</summary>
    /// <param name="secret">The password or OAuth bearer token from the OS credential store.</param>
    /// <returns>The mailbox options, holding the credential for the life of the run only.</returns>
    public ImapMailboxOptions ToImapOptions(string? secret) => new()
    {
        Host = Blank(ImapHost) ?? string.Empty,
        Port = ImapPort > 0 ? ImapPort : DefaultImapPort,
        Username = Blank(ImapUsername) ?? string.Empty,
        Password = secret ?? string.Empty,
        UseOAuthBearer = ImapUseOAuthBearer,
    };

    /// <summary>Checks the mailbox-shaped fields.</summary>
    /// <returns><see langword="null"/> when usable, otherwise the reason it is not.</returns>
    /// <remarks>
    /// <para><b>Plaintext IMAP is refused here, not warned about</b> (REQ-RAG-049). The library's
    /// <c>SocketImapConnection</c> only ever speaks implicit TLS, so a port that implies cleartext —
    /// 143, the STARTTLS port — cannot work and must fail at save time with the reason, rather than at
    /// 07:00 on a schedule with a socket error. This is the highest-sensitivity source in the product;
    /// a mailbox password does not go onto the wire in the clear because a field was left wrong.</para>
    /// <para>An <c>.mbox</c> archive needs none of this, so it is validated as a path and returns
    /// early — there is no server, no port and no credential in that shape.</para>
    /// </remarks>
    private string? ValidateEmail()
    {
        var host = Blank(ImapHost);
        var mbox = Blank(MboxPath);

        if (host is null && mbox is null)
        {
            return "A mail connector needs either an IMAP server or the path to a local .mbox file.";
        }

        if (host is not null && mbox is not null)
        {
            return "This connector names both an IMAP server and an .mbox file. Choose one — they are "
                + "different mailboxes, and reading both under one connector would merge two sources "
                + "into one set of citations.";
        }

        if (mbox is not null)
        {
            return Path.IsPathRooted(mbox)
                ? null
                : $"'{mbox}' is not a full path to an .mbox file.";
        }

        if (string.IsNullOrWhiteSpace(ImapUsername))
        {
            return "A mail connector needs the account name to sign in with, usually the address.";
        }

        var port = ImapPort > 0 ? ImapPort : DefaultImapPort;
        if (port is 143 or 110)
        {
            return $"Port {port} is a cleartext mail port, and this connector refuses it: your mailbox "
                + "password and every message would cross the network unencrypted. Use 993, the "
                + "implicit-TLS IMAP port.";
        }

        if (host!.StartsWith("imap://", StringComparison.OrdinalIgnoreCase))
        {
            return "'imap://' is the cleartext scheme and is refused. Enter the host name on its own — "
                + "the connection is always TLS.";
        }

        return null;
    }

    /// <summary>Renders the one-line mailbox summary. Never the account name paired with a secret.</summary>
    /// <param name="localize">Resolves the resource keys the summary is assembled from.</param>
    /// <returns>The summary line shown under a saved mail connector.</returns>
    /// <remarks>
    /// The date is formatted with <see cref="CultureInfo.InvariantCulture"/> deliberately. It is an
    /// ISO date the operator matches against a mail client's own filter, and letting it move with the
    /// UI culture would mean the same connector describing its own cut-off two different ways.
    /// </remarks>
    private string DescribeEmail(LocalizeText localize)
    {
        var where = IsMboxMailbox
            ? localize("ConnectorDescribeMailboxArchive", Path.GetFileName(MboxPath) ?? string.Empty)
            : localize(
                "ConnectorDescribeMailboxImap", ImapUsername ?? string.Empty, ImapHost ?? string.Empty);

        var folders = MailFolders.Count switch
        {
            0 => DefaultMailFolder,
            > 3 => localize(
                "ConnectorDescribeMailFoldersMore",
                string.Join(", ", MailFolders.Take(3)),
                MailFolders.Count - 3),
            _ => string.Join(", ", MailFolders),
        };

        var scope = MailSinceUtc is { } since
            ? localize(
                "ConnectorDescribeMailSince", since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            : string.Empty;

        var attachments = MailIncludeAttachments
            ? localize("ConnectorDescribeMailAttachments")
            : string.Empty;

        return $"{where} · {folders}{scope}{attachments}";
    }

    /// <summary>Checks the repository-shaped fields.</summary>
    private string? ValidateRepository()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            return "A repository connector needs a project path, for example 'owner/repository'.";
        }

        if (!ProjectPath.Contains('/', StringComparison.Ordinal))
        {
            return $"'{ProjectPath}' is not a project path; it needs an owner or group, "
                + "for example 'owner/repository'.";
        }

        return CheckUrl(ApiBaseUrl, "API base URL") ?? CheckUrl(WebBaseUrl, "web base URL");
    }

    /// <summary>Checks the Confluence-shaped fields.</summary>
    private string? ValidateConfluence()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return "A Confluence connector needs a site URL, for example "
                + "'https://acme.atlassian.net/wiki'.";
        }

        var badUrl = CheckUrl(BaseUrl, "site URL");
        if (badUrl is not null)
        {
            return badUrl;
        }

        var hasSpace = !string.IsNullOrWhiteSpace(SpaceKey);
        var hasRoot = !string.IsNullOrWhiteSpace(RootPageId);
        return hasSpace == hasRoot
            ? "A Confluence connector needs exactly one of a space key or a page id — a space is a "
                + "different walk from a page tree."
            : null;
    }

    /// <summary>
    /// Checks one operator-supplied URL, including whether it is asking to reach a private network
    /// without having said so.
    /// </summary>
    /// <param name="value">The URL, or <see langword="null"/> when not supplied.</param>
    /// <param name="label">What the URL is, for the message.</param>
    /// <returns><see langword="null"/> when usable, otherwise the reason it is not.</returns>
    /// <remarks>
    /// The private-network check here is a message, not a defence — a public name that resolves to
    /// loopback walks straight past it, and the enforcement that actually holds is the library's
    /// connect-time guarded handler. Refusing an obviously private literal at save time tells the
    /// operator which switch to turn on, instead of failing three days later as "connection refused".
    /// </remarks>
    private string? CheckUrl(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.TrimEnd('/'), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return $"The {label} must be an absolute http or https address.";
        }

        return !AllowPrivateNetwork && WebCrawlOptions.IsPrivateNetworkHost(uri.Host)
            ? $"'{uri.Host}' is on a private network. Turn on 'allow this connector to reach my own "
                + "network' if that is deliberate — it lets this connector send its credential to an "
                + "address inside your network."
            : null;
    }

    /// <summary>Normalizes an empty or whitespace string to null, which is what the library expects.</summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
