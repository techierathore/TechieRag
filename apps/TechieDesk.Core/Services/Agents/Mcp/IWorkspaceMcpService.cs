using TechieRag.Abstractions;
using TechieRag.Mcp;

namespace TechieDesk.Services.Agents.Mcp;

/// <summary>The outcome of a test connection to an MCP server (REQ-RAG-023).</summary>
/// <param name="IsSuccess">Whether the handshake completed and the tool list was read.</param>
/// <param name="Tools">What the server advertised, after the configured allow-list.</param>
/// <param name="Problems">
/// Why it failed — every validation problem at once, or the one connection failure. Never contains a
/// header or environment value.
/// </param>
public sealed record McpConnectionReport(
    bool IsSuccess,
    IReadOnlyList<McpAdvertisedTool> Tools,
    IReadOnlyList<string> Problems);

/// <summary>The outcome of saving a registration (REQ-RAG-023).</summary>
/// <param name="IsSuccess">Whether the registration was stored.</param>
/// <param name="Problems">Every reason it was refused, or empty on success.</param>
public sealed record McpSaveOutcome(bool IsSuccess, IReadOnlyList<string> Problems);

/// <summary>
/// One agent turn's MCP tools, and what it took to get them (REQ-RAG-023).
/// </summary>
/// <remarks>
/// Disposing this shuts down every MCP server the turn started — for a stdio server, that is the
/// child process. The local skill handler passed in is never disposed here; it was not created here.
/// </remarks>
public sealed class McpTurnTools : IAsyncDisposable
{
    private readonly McpWorkspaceTools? started;

    /// <summary>Gets the handler to give the agent loop: local skills plus this workspace's MCP tools.</summary>
    public IToolHandler ToolHandler { get; }

    /// <summary>Gets the names of the MCP servers that started and advertised their tools.</summary>
    public IReadOnlyList<string> StartedServers { get; }

    /// <summary>Gets the servers that were registered and enabled but could not be used.</summary>
    public IReadOnlyList<McpServerFailure> Failures { get; }

    /// <summary>Creates the turn's tool set.</summary>
    /// <param name="toolHandler">The composed handler.</param>
    /// <param name="started">The library's started-server set, disposed with this object.</param>
    /// <param name="startedServers">The servers that came up.</param>
    /// <param name="failures">The servers that did not.</param>
    internal McpTurnTools(
        IToolHandler toolHandler,
        McpWorkspaceTools? started,
        IReadOnlyList<string> startedServers,
        IReadOnlyList<McpServerFailure> failures)
    {
        ToolHandler = toolHandler;
        this.started = started;
        StartedServers = startedServers;
        Failures = failures;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (started is not null)
        {
            await started.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// The application's MCP surface: register servers, see what they offer, and hand their tools to a
/// chat turn (REQ-RAG-023 / BRD-86).
/// </summary>
/// <remarks>
/// <para><b>Everything protocol-shaped stays in the library.</b> This type stores registrations,
/// decides the trust policy, applies <see cref="EgressGate"/> and reports failures for the screen.
/// The handshake, tool discovery, tool invocation, transport choice and result bounding are
/// <c>McpClient</c>, <c>McpToolHandler</c> and <c>McpAgentExtensions</c> — none of it is
/// reimplemented here.</para>
/// <para><b>Nothing is contacted unless it was registered AND enabled.</b> A stock install with no
/// registration makes no connection of any kind from this service, which is what keeps REQ-NFR-008
/// true. Discovery is an explicit action (<see cref="TestAsync"/>); the Agents screen renders the
/// tool list from the cached row rather than by dialling every server on a page load.</para>
/// </remarks>
public interface IWorkspaceMcpService
{
    /// <summary>Gets which protection tier this build can actually keep MCP credentials in.</summary>
    McpCredentialProtection CredentialProtection { get; }

    /// <summary>Lists a workspace's registrations, contacting nothing.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The registrations, ordered by server name.</returns>
    Task<IReadOnlyList<McpServerRecord>> ListAsync(
        string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to the server a draft describes and reads its tool list, without saving anything.
    /// </summary>
    /// <param name="workspaceId">The workspace, used to resolve credentials already stored.</param>
    /// <param name="draft">The server being edited.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>What the server advertised, or why it could not be reached.</returns>
    /// <remarks>
    /// The point of testing before saving is that an administrator finds out a command path is wrong
    /// or a token has expired while they are looking at the form, not the next time somebody chats.
    /// A successful test against an already-registered server also refreshes its cached tool list.
    /// </remarks>
    Task<McpConnectionReport> TestAsync(
        string workspaceId, McpServerDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Stores a registration, replacing any server of the same name in this workspace.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="draft">The server being saved.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Success, or every reason the configuration was refused.</returns>
    Task<McpSaveOutcome> SaveAsync(
        string workspaceId, McpServerDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Removes a registration and its stored credentials.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="serverName">The configured server name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><see langword="true"/> when a registration was removed.</returns>
    Task<bool> RemoveAsync(
        string workspaceId, string serverName, CancellationToken cancellationToken = default);

    /// <summary>Suspends or resumes a registration without losing its configuration.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="serverName">The configured server name.</param>
    /// <param name="isEnabled">True to offer its tools to the agent.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the registration was found and updated.</returns>
    Task<bool> SetEnabledAsync(
        string workspaceId, string serverName, bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts this workspace's enabled MCP servers and composes their tools with the turn's local
    /// skills.
    /// </summary>
    /// <param name="workspaceId">The workspace answering the turn.</param>
    /// <param name="localTools">The REQ-RAG-022 skill registry already built for the turn.</param>
    /// <param name="gate">This turn's egress gate, which the HTTP servers' tools inherit.</param>
    /// <param name="cancellationToken">The turn's time-limit token.</param>
    /// <returns>The tool set for the loop; dispose it when the turn ends.</returns>
    /// <remarks>
    /// <para>Local skills win a name clash, so a registered server cannot shadow <c>rag-search</c>.</para>
    /// <para>Partial failure is normal and never fatal: one unreachable endpoint is reported in
    /// <see cref="McpTurnTools.Failures"/> and the rest of the workspace's tools still run.</para>
    /// </remarks>
    Task<McpTurnTools> BuildTurnToolsAsync(
        string workspaceId,
        IToolHandler localTools,
        EgressGate gate,
        CancellationToken cancellationToken = default);
}
