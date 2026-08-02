using System.Text.Json;

namespace TechieRag.Mcp;

/// <summary>
/// Carries JSON-RPC 2.0 messages to and from one MCP server (REQ-RAG-038).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Isolates <i>how</i> an MCP server is reached (child process, HTTP endpoint)
/// from <i>what</i> is said to it. <see cref="McpClient"/> speaks MCP; a transport only speaks
/// JSON-RPC framing.</para>
/// <para><b>New interface, not a widened one:</b> this is a new abstraction; no existing published
/// interface gained a member for MCP support. Hosts with an unusual transport (an in-process server,
/// a socket, a test double) implement this and hand it to <see cref="McpClient"/>.</para>
/// <para><b>Threading:</b> Implementations must tolerate concurrent callers. The built-in transports
/// serialise requests internally.</para>
/// <para><b>Trust:</b> A transport returns whatever the server sent. Nothing it returns is trusted;
/// <see cref="McpClient"/> validates shape and <see cref="McpToolHandler"/> bounds size.</para>
/// </remarks>
public interface IMcpTransport : IAsyncDisposable
{
    /// <summary>Gets the configured name of the server this transport reaches.</summary>
    string ServerName { get; }

    /// <summary>
    /// Establishes the connection — launching the child process or preparing the HTTP session.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the transport is ready to carry messages.</returns>
    /// <exception cref="McpException">The server could not be reached or started.</exception>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a JSON-RPC request and waits for its matching response.
    /// </summary>
    /// <param name="method">The JSON-RPC method name, e.g. <c>tools/list</c>.</param>
    /// <param name="parameters">The params object, or null to send none.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The <c>result</c> member of the response.</returns>
    /// <exception cref="McpException">The server returned a JSON-RPC error or an unparseable message.</exception>
    Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a JSON-RPC notification, for which no response is expected.
    /// </summary>
    /// <param name="method">The JSON-RPC method name, e.g. <c>notifications/initialized</c>.</param>
    /// <param name="parameters">The params object, or null to send none.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the notification has been written.</returns>
    /// <exception cref="McpException">The notification could not be delivered.</exception>
    Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken = default);
}
