namespace TechieDesk.Services.Agents.Mcp;

/// <summary>
/// Which of the three protection tiers an MCP credential actually landed in (REQ-FN-039 /
/// REQ-NFR-004b / REQ-FN-043).
/// </summary>
/// <remarks>
/// An enum rather than a prose string because the MCP tab has to render this in English and in
/// Hindi. A service that returned a sentence would be a service that decides the user's language.
/// </remarks>
public enum McpCredentialProtection
{
    /// <summary>The OS credential store — Keychain, or the DPAPI-backed Credential Manager.</summary>
    Keychain,

    /// <summary>
    /// The REQ-NFR-004b Data-Protection sidecar, used when the platform store refuses this build.
    /// </summary>
    EncryptedSidecar,

    /// <summary>
    /// Process memory only. Values are lost on exit and must be re-entered; no durable store was
    /// reachable and nothing was written to disk.
    /// </summary>
    MemoryOnly
}

/// <summary>
/// Where an MCP server's header and environment VALUES live — which is never the application
/// database (REQ-FN-039 applied to REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>What is secret here.</b> An HTTP MCP endpoint is reached with
/// <c>McpServerConfig.Headers</c>, which in practice means <c>Authorization: Bearer …</c>. A stdio
/// MCP server reads its credential out of <c>McpServerConfig.EnvironmentVariables</c>. Both are
/// replayable outbound credentials of exactly the class REQ-FN-039 exists for, so their values go to
/// the OS credential store and the <c>WorkspaceMcpServer</c> row keeps only the KEY NAMES.</para>
/// <para><b>Names are not secret and are deliberately kept.</b> Knowing that a server has an
/// <c>Authorization</c> header is what lets the screen say "its value could not be recovered from
/// the credential store — re-enter it" rather than sending an unauthenticated request and reporting
/// a bare 401.</para>
/// <para><b>Nothing behind this interface may write a value in cleartext, anywhere.</b> Not the
/// database, not a settings file, not a log line, not an exception message. Where no durable
/// platform store is reachable the degradation is defined and visible through
/// <see cref="IsDurable"/> — never a plaintext file that "works for now".</para>
/// </remarks>
public interface IMcpSecretStore
{
    /// <summary>Gets a value indicating whether stored values survive the process exiting.</summary>
    /// <remarks>
    /// False on an unsigned or un-entitled desktop build, where the platform keychain refuses the
    /// process (REQ-FN-043 is <c>Blocked</c> for exactly this reason on Mac Catalyst). The MCP tab
    /// must say so: a token that vanishes on restart is a tool server that starts failing overnight.
    /// </remarks>
    bool IsDurable { get; }

    /// <summary>Gets which protection tier values are actually being kept in.</summary>
    /// <remarks>
    /// Names the mechanism, never a value. The MCP tab renders this so an operator on an
    /// un-entitled build knows they are on the sidecar (or on nothing) before they save a token
    /// rather than after it disappears.
    /// </remarks>
    McpCredentialProtection Protection { get; }

    /// <summary>Reads one server's header/environment values.</summary>
    /// <param name="workspaceId">The workspace the server is registered in.</param>
    /// <param name="serverName">The configured server name.</param>
    /// <returns>
    /// The stored name/value pairs, or an empty map when there are none or they could not be
    /// recovered. Never null: an unrecoverable credential is an absent credential, not a crash.
    /// </returns>
    IReadOnlyDictionary<string, string> Read(string workspaceId, string serverName);

    /// <summary>Stores (or replaces) one server's header/environment values.</summary>
    /// <param name="workspaceId">The workspace the server is registered in.</param>
    /// <param name="serverName">The configured server name.</param>
    /// <param name="secrets">The name/value pairs. Null or empty removes the stored values.</param>
    void Write(string workspaceId, string serverName, IReadOnlyDictionary<string, string>? secrets);

    /// <summary>Removes one server's stored values.</summary>
    /// <param name="workspaceId">The workspace the server is registered in.</param>
    /// <param name="serverName">The configured server name.</param>
    /// <returns><see langword="true"/> when something was present and removed.</returns>
    bool Delete(string workspaceId, string serverName);
}
