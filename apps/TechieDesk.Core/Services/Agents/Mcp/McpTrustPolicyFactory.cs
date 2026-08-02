using TechieRag.Mcp;

namespace TechieDesk.Services.Agents.Mcp;

/// <summary>
/// TechieDesk's answer to the one question <see cref="McpTrustPolicy"/> asks the host application
/// (REQ-RAG-023 / REQ-RAG-038).
/// </summary>
/// <remarks>
/// <para><b>Why this is code and not configuration.</b> The library deliberately refuses to bind a
/// trust policy from configuration: permitting a local process launch is arbitrary code execution,
/// and a settings file that could turn it on would mean an edited JSON file is enough to make the
/// app run anything. The decision therefore has to be stated here, in a compiled type, where it is
/// reviewable.</para>
/// <para><b>Local process launch is permitted, and that is a deliberate, narrow decision.</b> Nearly
/// every MCP server in existence is a stdio child process; a desktop application that refused them
/// would ship a registration screen that could register almost nothing. What makes it acceptable is
/// what the library still enforces around it and what this application adds:</para>
/// <list type="bullet">
/// <item><description>the executable must be a FULLY-QUALIFIED path — a bare <c>npx</c> is refused,
/// so what runs cannot be decided by the user's <c>PATH</c>;</description></item>
/// <item><description>arguments are a LIST, never a command line, so there is no shell and nothing
/// to quote;</description></item>
/// <item><description>a server is only ever launched because an administrator registered it in this
/// workspace and left it enabled — nothing is discovered, imported or auto-started;</description></item>
/// <item><description>a stock install with no registration launches nothing at all, which is what
/// keeps REQ-NFR-008's zero-egress-by-default guarantee true.</description></item>
/// </list>
/// <para><b>Plaintext HTTP stays refused beyond loopback.</b> An <c>http://</c> endpoint on another
/// host would put the server's bearer token on the wire in the clear. Loopback is always allowed
/// because it never leaves the machine, which is also what makes a locally hosted MCP server a
/// first-class option rather than a workaround.</para>
/// <para><b>No command-directory allow-list is imposed.</b> TechieDesk neither ships nor downloads
/// MCP servers, so there is no "one of the binaries we manage" folder to point
/// <see cref="McpTrustPolicy.AllowedCommandDirectories"/> at. Naming one anyway would look like a
/// containment boundary while containing nothing.</para>
/// </remarks>
public static class McpTrustPolicyFactory
{
    /// <summary>
    /// Gets the policy every MCP registration in this application is validated and launched under.
    /// </summary>
    /// <remarks>
    /// A single shared instance: the registry validates against it on the way in and
    /// <c>McpClient.Create</c> re-applies it at the moment a transport is built, so a row that
    /// somehow reached the database cannot be started under a laxer rule than it was written under.
    /// </remarks>
    public static McpTrustPolicy Desktop { get; } = new()
    {
        AllowLocalProcessLaunch = true,
        AllowPlaintextHttp = false
    };
}
