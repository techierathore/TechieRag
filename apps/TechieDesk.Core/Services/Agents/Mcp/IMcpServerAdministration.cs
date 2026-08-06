using TechieRag.Mcp;

namespace TechieDesk.Services.Agents.Mcp;

/// <summary>
/// The app-only half of MCP registration storage: everything the Agents screen needs that the
/// library's four-method <see cref="IMcpServerRegistry"/> deliberately does not carry (REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>Why not widen <see cref="IMcpServerRegistry"/>.</b> That interface is published in the
/// library and its own documentation states the reason it stayed small: it answers one question —
/// which servers may this workspace's agent use — and any host may implement it over any store.
/// Credential key names and a cached tool list are TechieDesk's presentation concerns, not part of
/// that contract, and adding them would break every existing implementer for a feature none of them
/// asked for.</para>
/// <para><b>One class implements both.</b> <see cref="SqliteMcpServerRegistry"/> is registered under
/// this interface and under <see cref="IMcpServerRegistry"/>, so the agent loop reads through the
/// library contract and the screen reads through this one, and there is exactly one table.</para>
/// </remarks>
public interface IMcpServerAdministration
{
    /// <summary>
    /// Lists a workspace's registrations with the app-only facts attached.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The registrations, ordered by server name. Contacts no server.</returns>
    Task<IReadOnlyList<McpServerRecord>> ListRecordsAsync(
        string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Caches what a server was observed advertising, so the screen can show its tools without
    /// contacting it again.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="serverName">The configured server name.</param>
    /// <param name="tools">The tools the server advertised.</param>
    /// <param name="observedAtUtc">When the observation was made (UTC).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><see langword="true"/> when a registration was found and updated.</returns>
    Task<bool> RecordDiscoveredToolsAsync(
        string workspaceId,
        string serverName,
        IReadOnlyList<McpToolDescriptor> tools,
        DateTime observedAtUtc,
        CancellationToken cancellationToken = default);
}
