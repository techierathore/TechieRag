using TechieRag.Mcp;

namespace TechieDesk.Services.Agents.Mcp;

/// <summary>One tool a registered MCP server was last seen advertising (REQ-RAG-023).</summary>
/// <param name="Name">The tool name as the server advertises it.</param>
/// <param name="Description">What the tool does, as the server describes it.</param>
/// <remarks>
/// Cached from the last <c>tools/list</c> so the Agents screen can show what a server offers without
/// contacting it on every page load. It is the server's own text and is treated as untrusted display
/// data, never as an instruction.
/// </remarks>
public sealed record McpAdvertisedTool(string Name, string Description);

/// <summary>
/// Everything the MCP tab knows about one stored registration — the library's registration plus the
/// app-only facts the library has no place for (REQ-RAG-023).
/// </summary>
/// <param name="Registration">The library registration, with any recoverable credentials attached.</param>
/// <param name="SecretKeyNames">
/// The header or environment-variable NAMES the administrator configured, read from the row rather
/// than from the credential store.
/// </param>
/// <param name="AdvertisedTools">What the server was last seen advertising, or an empty list.</param>
/// <param name="LastCheckedUtc">When that tool list was observed, or null if never.</param>
/// <remarks>
/// <para><b>Why the names are separate from the values.</b> <see cref="SecretKeyNames"/> comes from
/// the database and always survives; the VALUES come from the OS credential store and may not. When
/// a name is present with no matching value in
/// <see cref="McpServerConfig.Headers"/>/<see cref="McpServerConfig.EnvironmentVariables"/>, the
/// credential was not recoverable — which the screen says out loud instead of letting the server be
/// called unauthenticated (REQ-FN-043).</para>
/// </remarks>
public sealed record McpServerRecord(
    McpServerRegistration Registration,
    IReadOnlyList<string> SecretKeyNames,
    IReadOnlyList<McpAdvertisedTool> AdvertisedTools,
    DateTime? LastCheckedUtc)
{
    /// <summary>Gets the configured server name.</summary>
    public string ServerName => Registration.Server.Name;

    /// <summary>Gets whether the server's tools are offered to the agent.</summary>
    public bool IsEnabled => Registration.IsEnabled;

    /// <summary>
    /// Gets the credential names that were configured but whose values could not be recovered.
    /// </summary>
    /// <returns>The names to warn about; empty when every configured credential resolved.</returns>
    public IReadOnlyList<string> UnrecoverableSecretKeyNames()
    {
        var supplied = Registration.Server.Transport == McpTransportKind.Stdio
            ? Registration.Server.EnvironmentVariables.Keys
            : Registration.Server.Headers.Keys;

        var present = new HashSet<string>(supplied, StringComparer.OrdinalIgnoreCase);
        return SecretKeyNames.Where(name => !present.Contains(name)).ToList();
    }
}
