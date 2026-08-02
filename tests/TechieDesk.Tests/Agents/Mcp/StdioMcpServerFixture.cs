namespace TechieDesk.Tests.Agents.Mcp;

/// <summary>
/// A REAL stdio MCP server: a shell script launched as a child process that speaks
/// newline-delimited JSON-RPC over its own stdin and stdout (REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>Why a child process and not another fake transport.</b> The stdio path is the one that
/// actually creates a process, applies <c>McpTrustPolicy.AllowLocalProcessLaunch</c>, passes the
/// environment through, and has to shut the process down afterwards. None of that is exercised by an
/// in-memory <c>IMcpTransport</c>, and all of it is what a registration screen would break.</para>
/// <para><b>The token proves the credential path.</b> The script echoes
/// <c>MCPFIXTURETOKEN</c> — an environment variable that starts life as a value typed into the
/// registration editor, goes to <see cref="TechieDesk.Services.Agents.Mcp.IMcpSecretStore"/>, comes
/// back out on the next read, and is only visible in the tool's answer if it survived every hop. A
/// stored-and-never-used credential would still pass a round-trip assertion on the store; it cannot
/// pass this one.</para>
/// <para><b>POSIX only.</b> The script is driven through <c>/bin/sh</c>, so tests using it check for
/// it and skip on a platform that has none rather than asserting something vacuous.</para>
/// </remarks>
public sealed class StdioMcpServerFixture : IDisposable
{
    /// <summary>The name of the environment variable the fixture reads its token from.</summary>
    public const string TokenVariableName = "MCPFIXTURETOKEN";

    /// <summary>The tool the fixture advertises.</summary>
    public const string ToolName = "whoami";

    /// <summary>The absolute path of the shell every stdio fixture is launched through.</summary>
    public const string ShellPath = "/bin/sh";

    private readonly string directory;

    /// <summary>Writes the script into a temporary directory.</summary>
    public StdioMcpServerFixture()
    {
        directory = Path.Combine(Path.GetTempPath(), $"techiedesk-mcp-stdio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        ScriptPath = Path.Combine(directory, "mcpfixture.sh");
        File.WriteAllText(ScriptPath, Script);
    }

    /// <summary>Gets the absolute path of the generated script.</summary>
    public string ScriptPath { get; }

    /// <summary>Gets whether this platform can run the fixture at all.</summary>
    public static bool IsSupported => File.Exists(ShellPath);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
    }

    /// <summary>
    /// The server itself: read a JSON-RPC line, pull its id out, answer the three MCP methods the
    /// client uses, ignore everything else, and exit when stdin closes.
    /// </summary>
    private const string Script = """
        while IFS= read -r line; do
          id=$(printf '%s' "$line" | awk 'match($0,/"id":[0-9]+/){print substr($0,RSTART+5,RLENGTH-5)}')
          case "$line" in
            *'"method":"initialize"'*)
              printf '{"jsonrpc":"2.0","id":%s,"result":{"protocolVersion":"2025-06-18","serverInfo":{"name":"stdio-fixture"}}}\n' "$id"
              ;;
            *'"method":"tools/list"'*)
              printf '{"jsonrpc":"2.0","id":%s,"result":{"tools":[{"name":"whoami","description":"Reports the fixture identity and the token it was started with.","inputSchema":{"type":"object","properties":{}}}]}}\n' "$id"
              ;;
            *'"method":"tools/call"'*)
              printf '{"jsonrpc":"2.0","id":%s,"result":{"content":[{"type":"text","text":"stdio fixture token=%s"}],"isError":false}}\n' "$id" "$MCPFIXTURETOKEN"
              ;;
            *)
              ;;
          esac
        done
        """;
}
