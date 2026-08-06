namespace TechieRag.Mcp;

/// <summary>
/// The host's trust decision about what an MCP server configuration is allowed to do
/// (REQ-RAG-038 / REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>Why this type exists:</b> An MCP server is an external process or an external HTTP
/// endpoint. Launching a local process named by user-supplied configuration is arbitrary code
/// execution by definition. The library therefore refuses to do it unless the host application
/// has said so explicitly, in code, by constructing a policy that permits it — a configuration
/// file alone can never turn it on, because the policy is not bound from configuration.</para>
/// <para><b>Trust model in one paragraph:</b> The library trusts the <i>host application</i> to
/// decide which commands and endpoints are acceptable. It does not trust the <i>configuration</i>,
/// the <i>MCP server</i>, or the <i>LLM</i>. Configuration is validated against this policy before
/// anything is started; the server is treated as an untrusted producer of text (its tool output is
/// data, never instructions the library acts on); and the LLM may only name tools the server
/// actually advertised and the policy actually allowed.</para>
/// <para><b>Defaults are closed:</b> <see cref="Strict"/> permits no process launch and no
/// plaintext HTTP to a non-loopback host.</para>
/// </remarks>
public sealed class McpTrustPolicy
{
    /// <summary>
    /// Gets the closed default: remote HTTPS MCP servers only — no local process launch,
    /// no plaintext HTTP beyond loopback.
    /// </summary>
    /// <remarks>Use this unless the host application has a specific reason to relax it.</remarks>
    public static McpTrustPolicy Strict { get; } = new();

    /// <summary>
    /// Gets whether the host permits launching a local child process for
    /// <see cref="McpTransportKind.Stdio"/> servers.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/>. When false, a stdio server configuration is rejected at
    /// validation time and no process is ever created. Setting this to true is a deliberate
    /// statement that the host has its own control over where the configured commands come from.
    /// </remarks>
    public bool AllowLocalProcessLaunch { get; init; }

    /// <summary>
    /// Gets the directories a stdio server's executable must live under, or an empty list to
    /// accept any fully-qualified path.
    /// </summary>
    /// <remarks>
    /// A desktop application that ships or downloads its MCP servers into a known folder should set
    /// this to that folder. It converts "any absolute path the config names" into "one of the
    /// binaries we manage", which is the difference between a launcher and an execution primitive.
    /// Comparison is by normalised full path prefix.
    /// </remarks>
    public IReadOnlyList<string> AllowedCommandDirectories { get; init; } = [];

    /// <summary>
    /// Gets whether an <c>http://</c> (non-TLS) endpoint is accepted for a non-loopback host.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/>. Loopback (<c>localhost</c>, <c>127.0.0.1</c>, <c>::1</c>)
    /// is always allowed over plaintext because it never leaves the machine; anything else would put
    /// the server's bearer token on the wire in the clear.
    /// </remarks>
    public bool AllowPlaintextHttp { get; init; }

    /// <summary>
    /// Gets the maximum number of characters of a tool result that will be handed back to the
    /// agent loop; longer results are truncated with a visible marker.
    /// </summary>
    /// <remarks>
    /// An MCP server is untrusted and can return an unbounded response. Without a cap, one tool call
    /// can blow the model's context window and silently evict the conversation. Defaults to 100000.
    /// </remarks>
    public int MaxToolResultCharacters { get; init; } = 100000;

    /// <summary>
    /// Determines whether the given executable path satisfies <see cref="AllowedCommandDirectories"/>.
    /// </summary>
    /// <param name="commandPath">A fully-qualified executable path.</param>
    /// <returns><see langword="true"/> when the path is permitted by the directory allow-list.</returns>
    public bool IsCommandDirectoryAllowed(string commandPath)
    {
        if (string.IsNullOrEmpty(commandPath)) return false;
        if (AllowedCommandDirectories.Count == 0) return true;

        var fullPath = Path.GetFullPath(commandPath);
        foreach (var directory in AllowedCommandDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;

            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(root, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
