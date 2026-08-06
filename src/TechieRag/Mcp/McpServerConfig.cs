using System.Text.RegularExpressions;

namespace TechieRag.Mcp;

/// <summary>How the library reaches an MCP server.</summary>
public enum McpTransportKind
{
    /// <summary>
    /// A local child process speaking newline-delimited JSON-RPC over stdin/stdout.
    /// </summary>
    /// <remarks>
    /// Requires <see cref="McpTrustPolicy.AllowLocalProcessLaunch"/>. This is the transport that
    /// executes code on the user's machine.
    /// </remarks>
    Stdio,

    /// <summary>
    /// A remote MCP endpoint reached with HTTP POST (the streamable-HTTP transport).
    /// </summary>
    /// <remarks>No process is created. This is the default-safe transport.</remarks>
    Http
}

/// <summary>
/// A validated description of one MCP server the agent may call tools through (REQ-RAG-038).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries everything needed to reach one MCP server, and nothing that would
/// let it be reached in an unintended way. In particular a stdio server is described as an
/// executable path plus a <i>list</i> of arguments — never as one command string — so there is no
/// shell, no quoting to get wrong, and no argument injection through a value that happens to
/// contain a space or a semicolon.</para>
/// <para><b>Trust:</b> This object is data supplied by a user or an administrator and is treated as
/// untrusted. Nothing here is acted on until <see cref="Validate(McpTrustPolicy)"/> has passed
/// against the host's <see cref="McpTrustPolicy"/>.</para>
/// <para><b>Credentials:</b> <see cref="Headers"/> and <see cref="EnvironmentVariables"/> routinely
/// hold API tokens. Their values are never logged and never appear in exception messages or in
/// <see cref="Describe"/>; only their key names do.</para>
/// </remarks>
public sealed class McpServerConfig
{
    private static readonly Regex NamePattern = new("^[A-Za-z0-9][A-Za-z0-9-]{0,47}$", RegexOptions.Compiled);

    /// <summary>
    /// Gets the short server name, used to qualify its tool names for the LLM.
    /// </summary>
    /// <remarks>
    /// Constrained to letters, digits and hyphens (1-48 characters) because it becomes part of the
    /// tool name sent to the model, and mainstream providers accept only <c>[A-Za-z0-9_-]</c> there.
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>Gets the transport used to reach this server.</summary>
    public required McpTransportKind Transport { get; init; }

    /// <summary>
    /// Gets the fully-qualified path of the executable to launch, for
    /// <see cref="McpTransportKind.Stdio"/>.
    /// </summary>
    /// <remarks>
    /// Must be an absolute path. A bare name such as <c>npx</c> is rejected: resolving it would mean
    /// searching <c>PATH</c>, which makes what actually runs depend on the user's environment rather
    /// than on the configuration that was reviewed.
    /// </remarks>
    public string? Command { get; init; }

    /// <summary>Gets the argument list passed to <see cref="Command"/>, one element per argument.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Gets extra environment variables for the child process (values are never logged).</summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the working directory for the child process, or null to inherit the host's.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Gets the absolute endpoint URL, for <see cref="McpTransportKind.Http"/>.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Gets extra HTTP headers, typically authorization (values are never logged).</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the per-request timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Gets the tool names this server is allowed to expose, or an empty list to expose everything
    /// it advertises.
    /// </summary>
    /// <remarks>
    /// The allow-list is applied twice: advertised tools outside it are never shown to the model,
    /// and a call to a tool outside it is refused even if the model names it anyway. A server that
    /// grows a new destructive tool in an update therefore cannot quietly acquire it.
    /// </remarks>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    /// <summary>
    /// Collects every reason this configuration is unusable under the given policy.
    /// </summary>
    /// <param name="policy">The host's trust policy.</param>
    /// <returns>An empty list when the configuration is acceptable, otherwise every problem found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
    public IReadOnlyList<string> FindProblems(McpTrustPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Name) || !NamePattern.IsMatch(Name))
        {
            problems.Add("Name must be 1-48 characters of letters, digits or hyphens, starting with a letter or digit.");
        }

        if (TimeoutSeconds is < 1 or > 3600)
        {
            problems.Add("TimeoutSeconds must be between 1 and 3600.");
        }

        if (Transport == McpTransportKind.Stdio)
        {
            FindStdioProblems(policy, problems);
        }
        else
        {
            FindHttpProblems(policy, problems);
        }

        return problems;
    }

    /// <summary>
    /// Validates this configuration against the host's trust policy, throwing when it is unusable.
    /// </summary>
    /// <param name="policy">The host's trust policy.</param>
    /// <exception cref="McpConfigurationException">Thrown when the configuration is rejected; the
    /// exception lists every problem found rather than only the first.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
    public void Validate(McpTrustPolicy policy)
    {
        var problems = FindProblems(policy);
        if (problems.Count > 0)
        {
            throw new McpConfigurationException(Name ?? string.Empty, problems);
        }
    }

    /// <summary>
    /// Produces a log-safe one-line description of this server.
    /// </summary>
    /// <returns>The name, transport and target, plus the <i>names</i> of any headers or environment
    /// variables supplied — never their values.</returns>
    /// <remarks>Use this anywhere a server would otherwise be logged; <c>ToString</c> defers to it.</remarks>
    public string Describe()
    {
        var target = Transport == McpTransportKind.Stdio
            ? $"command={Command} args={Arguments.Count}"
            : $"endpoint={Endpoint}";

        var secretKeys = Transport == McpTransportKind.Stdio ? EnvironmentVariables.Keys : Headers.Keys;
        var keys = secretKeys.ToList();
        var secrets = keys.Count == 0 ? "none" : string.Join(",", keys);

        return $"mcp[{Name}] transport={Transport} {target} secretKeys={secrets}";
    }

    /// <inheritdoc/>
    public override string ToString() => Describe();

    private void FindStdioProblems(McpTrustPolicy policy, List<string> problems)
    {
        if (!policy.AllowLocalProcessLaunch)
        {
            problems.Add(
                "Stdio transport launches a local process; the host's McpTrustPolicy does not allow it "
                + "(set AllowLocalProcessLaunch in code to permit it).");
        }

        if (string.IsNullOrWhiteSpace(Command))
        {
            problems.Add("Command is required for stdio transport.");
        }
        else if (!Path.IsPathFullyQualified(Command))
        {
            problems.Add("Command must be a fully-qualified path; bare executable names are refused because PATH lookup would decide what actually runs.");
        }
        else if (Command.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
        {
            problems.Add("Command contains a control character.");
        }
        else if (!policy.IsCommandDirectoryAllowed(Command))
        {
            problems.Add("Command is outside every directory listed in the host's McpTrustPolicy.AllowedCommandDirectories.");
        }

        if (Arguments.Any(argument => argument is null))
        {
            problems.Add("Arguments must not contain null entries.");
        }

        if (!string.IsNullOrWhiteSpace(Endpoint))
        {
            problems.Add("Endpoint must not be set for stdio transport.");
        }

        if (WorkingDirectory is not null && !Path.IsPathFullyQualified(WorkingDirectory))
        {
            problems.Add("WorkingDirectory must be a fully-qualified path when supplied.");
        }
    }

    private void FindHttpProblems(McpTrustPolicy policy, List<string> problems)
    {
        if (!string.IsNullOrWhiteSpace(Command))
        {
            problems.Add("Command must not be set for http transport.");
        }

        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            problems.Add("Endpoint is required for http transport.");
            return;
        }

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri))
        {
            problems.Add("Endpoint must be an absolute URL.");
            return;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add("Endpoint must use http or https.");
            return;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback && !policy.AllowPlaintextHttp)
        {
            problems.Add("Endpoint uses plaintext http to a non-loopback host, which would put its credentials on the wire in the clear.");
        }
    }
}
