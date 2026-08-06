namespace TechieRag.Mcp;

/// <summary>
/// Thrown when an MCP server returns a protocol-level error or speaks something that is not MCP.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Distinguishes "the server said no" and "the server is broken" from
/// ordinary transport failures, so callers can tell a misconfigured server from an unreachable one.</para>
/// <para><b>Deliberately opaque:</b> Messages carry the server name and the JSON-RPC error text.
/// They never carry request headers, environment variables, or arguments, any of which may hold
/// credentials.</para>
/// </remarks>
public class McpException : Exception
{
    /// <summary>Gets the configured name of the MCP server involved, when known.</summary>
    public string? ServerName { get; }

    /// <summary>Gets the JSON-RPC error code returned by the server, when the failure was a JSON-RPC error.</summary>
    public int? ErrorCode { get; }

    /// <summary>Creates an MCP exception with a message.</summary>
    /// <param name="message">Description of the failure.</param>
    public McpException(string message) : base(message)
    {
    }

    /// <summary>Creates an MCP exception with a message and inner cause.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying cause.</param>
    public McpException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Creates an MCP exception attributed to a specific server.</summary>
    /// <param name="serverName">The configured MCP server name.</param>
    /// <param name="message">Description of the failure.</param>
    /// <param name="errorCode">The JSON-RPC error code, when applicable.</param>
    /// <param name="innerException">The underlying cause, when applicable.</param>
    public McpException(string serverName, string message, int? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ServerName = serverName;
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Thrown when an <see cref="McpServerConfig"/> is rejected before anything is started.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Configuration is validated up front against an <see cref="McpTrustPolicy"/>.
/// Every reason the configuration was refused is reported together in <see cref="Problems"/>, so a
/// user fixing a server definition sees the whole list rather than one error per attempt.</para>
/// </remarks>
public sealed class McpConfigurationException : McpException
{
    /// <summary>Gets every reason the configuration was rejected.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>Creates a configuration exception listing all validation problems.</summary>
    /// <param name="serverName">The configured MCP server name (may be blank when the name itself is invalid).</param>
    /// <param name="problems">The validation problems found.</param>
    public McpConfigurationException(string serverName, IReadOnlyList<string> problems)
        : base(serverName, $"MCP server '{serverName}' is not usable: {string.Join(" ", problems)}")
    {
        Problems = problems;
    }
}
