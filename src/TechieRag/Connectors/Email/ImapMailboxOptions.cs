namespace TechieRag.Connectors.Email;

/// <summary>
/// How to reach and authenticate to an IMAP server (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>Credentials are inputs.</b> <see cref="Password"/> is supplied by the caller from
/// wherever it already stores secrets — TechieDesk reads it from the OS keychain through its own
/// <c>ISecretStore</c> — and lives in memory for the connection's lifetime only. TechieRag has no
/// secret store, never writes this value to disk, and never includes it in a log line or an
/// exception message. Do not persist a populated instance of this class.</para>
/// </remarks>
public sealed class ImapMailboxOptions
{
    /// <summary>Gets or sets the server host name.</summary>
    /// <remarks>For example <c>imap.gmail.com</c> or <c>outlook.office365.com</c>.</remarks>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the port.</summary>
    /// <remarks>
    /// 993, implicit TLS. Port 143 with STARTTLS is not supported: every provider BRD-135 names
    /// offers 993, and a code path that begins in plaintext is a code path that can be talked out of
    /// upgrading. One always-encrypted path is a smaller thing to get right.
    /// </remarks>
    public int Port { get; set; } = 993;

    /// <summary>Gets or sets the account name to authenticate as.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Gets or sets the password or OAuth 2.0 access token.</summary>
    /// <remarks>Supply this from your own secret store; see the remarks on this class.</remarks>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets whether <see cref="Password"/> is an OAuth 2.0 access token.</summary>
    /// <remarks>
    /// Set for Gmail and Microsoft 365, which no longer accept a password over IMAP at all.
    /// Authenticates with the <c>XOAUTH2</c> mechanism instead of <c>LOGIN</c>.
    /// </remarks>
    public bool UseOAuthBearer { get; set; }

    /// <summary>Gets or sets the connect, read and write timeout, applied to each operation individually.</summary>
    /// <remarks>A server that accepts the connection and then stops responding fails this deadline rather than holding the run open.</remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets the most bytes one server response may deliver through counted literals.</summary>
    /// <remarks>
    /// <para>A hostile or compromised server announces a literal's length before sending it — the
    /// client allocates that much and then reads. A declared <c>{2000000000}</c> is therefore a
    /// two-gigabyte allocation the server chose, and a request the client must refuse rather than
    /// honour. This bound is what makes it refusable.</para>
    /// <para>The default is comfortably above the largest message any of the providers BRD-135 names
    /// will deliver. Raise it only if a real mailbox genuinely holds bigger mail; a message beyond
    /// the bound fails with an operator-facing reason rather than being silently skipped.</para>
    /// </remarks>
    public long MaxMessageBytes { get; set; } = 64 * 1024 * 1024;
}
