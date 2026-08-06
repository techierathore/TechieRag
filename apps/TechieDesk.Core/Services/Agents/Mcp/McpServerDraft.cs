using TechieRag.Mcp;

namespace TechieDesk.Services.Agents.Mcp;

/// <summary>
/// One credential the administrator typed for an MCP server: a header name, or an environment
/// variable name, and its value (REQ-RAG-023).
/// </summary>
/// <remarks>
/// A mutable class rather than a record because the editor two-way binds both fields. The
/// <see cref="Value"/> is a secret from the moment it is typed: it goes to
/// <see cref="IMcpSecretStore"/> and never to the database, a log line, or an exception message.
/// </remarks>
public sealed class McpCredentialEntry
{
    /// <summary>Gets or sets the header or environment-variable name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the value. Blank means "keep whatever is already stored".</summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// The editable form of one MCP server registration, as the Agents screen holds it (REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>Why a draft type and not <see cref="McpServerConfig"/> directly.</b> The library
/// configuration is immutable, validated, and models arguments and the tool allow-list as lists —
/// which is exactly right for something about to launch a process, and unusable as the backing store
/// for a text box. The draft is the mutable, text-shaped thing a person edits;
/// <see cref="ToConfig"/> is the single place it becomes the validated object, so the parsing rules
/// live in one tested method instead of in a Razor expression.</para>
/// <para><b>Arguments are split on NEWLINES, never on spaces.</b> The library refuses to accept a
/// stdio server as one command string precisely so nothing has to guess where an argument ends. A
/// UI that split on spaces would put that guess straight back, and would break the first path
/// containing a space. One argument per line is unambiguous and needs no quoting.</para>
/// </remarks>
public sealed class McpServerDraft
{
    /// <summary>Gets or sets the short server name that qualifies its tool names for the model.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets how the server is reached.</summary>
    public McpTransportKind Transport { get; set; } = McpTransportKind.Http;

    /// <summary>Gets or sets the fully-qualified executable path, for a stdio server.</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Gets or sets the argument list, one argument per line.</summary>
    public string ArgumentsText { get; set; } = string.Empty;

    /// <summary>Gets or sets the child process's working directory, or blank to inherit.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the absolute endpoint URL, for an HTTP server.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the tool allow-list, one tool name per line; blank allows everything.</summary>
    public string AllowedToolsText { get; set; } = string.Empty;

    /// <summary>Gets or sets the per-request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Gets or sets whether the server's tools are offered to the agent.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets whether this draft edits a server that already exists.</summary>
    public bool IsExisting { get; private set; }

    /// <summary>Gets the credentials the administrator entered.</summary>
    public List<McpCredentialEntry> Credentials { get; } = [];

    /// <summary>Adds an empty credential row for the editor to bind to.</summary>
    public void AddCredential() => Credentials.Add(new McpCredentialEntry());

    /// <summary>
    /// Builds a draft for a new server, defaulting to the transport that cannot execute anything.
    /// </summary>
    /// <returns>An empty draft.</returns>
    /// <remarks>
    /// HTTP is the default because it is the library's default-safe transport: choosing stdio has to
    /// be a decision someone makes, not the shape the form happens to open in.
    /// </remarks>
    public static McpServerDraft ForNewServer() => new();

    /// <summary>
    /// Builds a draft from a stored registration, carrying over the credential NAMES.
    /// </summary>
    /// <param name="record">The stored registration.</param>
    /// <returns>A draft the editor can bind to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record"/> is null.</exception>
    /// <remarks>
    /// Credential VALUES are deliberately left blank even when they were recoverable. Re-displaying
    /// a bearer token in a text box puts it on screen, into the DOM, and into any screenshot of the
    /// window for no benefit — a blank value means "keep what is stored", and
    /// <see cref="ToConfig"/> merges the existing values back in.
    /// </remarks>
    public static McpServerDraft From(McpServerRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var server = record.Registration.Server;
        var draft = new McpServerDraft
        {
            IsExisting = true,
            Name = server.Name,
            Transport = server.Transport,
            Command = server.Command ?? string.Empty,
            ArgumentsText = string.Join(Environment.NewLine, server.Arguments),
            WorkingDirectory = server.WorkingDirectory ?? string.Empty,
            Endpoint = server.Endpoint ?? string.Empty,
            AllowedToolsText = string.Join(Environment.NewLine, server.AllowedTools),
            TimeoutSeconds = server.TimeoutSeconds,
            IsEnabled = record.IsEnabled
        };

        foreach (var name in record.SecretKeyNames)
        {
            draft.Credentials.Add(new McpCredentialEntry { Name = name });
        }

        return draft;
    }

    /// <summary>
    /// Turns the draft into the validated library configuration.
    /// </summary>
    /// <param name="existingSecrets">
    /// Values already stored for this server, used to fill any credential row left blank so an edit
    /// that only changes the endpoint does not silently erase the token.
    /// </param>
    /// <returns>The configuration, not yet validated against the trust policy.</returns>
    /// <remarks>
    /// Fields belonging to the OTHER transport are dropped rather than carried: the library rejects a
    /// configuration that sets both an endpoint and a command, and a user who switched a server from
    /// HTTP to stdio should not have to clear the old field to make the form save.
    /// </remarks>
    public McpServerConfig ToConfig(IReadOnlyDictionary<string, string>? existingSecrets = null)
    {
        var isStdio = Transport == McpTransportKind.Stdio;
        var secrets = ResolveSecrets(existingSecrets, isStdio);

        return new McpServerConfig
        {
            Name = Name.Trim(),
            Transport = Transport,
            Command = isStdio ? NullIfBlank(Command) : null,
            Arguments = isStdio ? SplitLines(ArgumentsText) : [],
            WorkingDirectory = isStdio ? NullIfBlank(WorkingDirectory) : null,
            Endpoint = isStdio ? null : NullIfBlank(Endpoint),
            AllowedTools = SplitLines(AllowedToolsText),
            TimeoutSeconds = TimeoutSeconds,
            EnvironmentVariables = isStdio
                ? secrets
                : new Dictionary<string, string>(StringComparer.Ordinal),
            Headers = isStdio
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : secrets
        };
    }

    /// <summary>Merges typed values over already-stored ones, dropping unnamed rows.</summary>
    /// <param name="existingSecrets">Values already stored for this server.</param>
    /// <param name="isStdio">Whether the target dictionary is case-sensitive (environment) or not (headers).</param>
    /// <returns>The credential map to store and to send.</returns>
    private Dictionary<string, string> ResolveSecrets(
        IReadOnlyDictionary<string, string>? existingSecrets, bool isStdio)
    {
        // Environment variable names are case-sensitive on the platforms this ships to; HTTP header
        // names are not. Using the wrong comparer would let "authorization" and "Authorization"
        // become two headers on one request.
        var comparer = isStdio ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var resolved = new Dictionary<string, string>(comparer);

        foreach (var entry in Credentials)
        {
            var name = entry.Name?.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            if (!string.IsNullOrEmpty(entry.Value))
            {
                resolved[name] = entry.Value;
                continue;
            }

            // Blank means "keep what is stored". When nothing is stored — an un-entitled build that
            // lost the keychain — the name survives on the row and the screen reports it as
            // unrecoverable rather than the value being invented.
            if (existingSecrets is not null && existingSecrets.TryGetValue(name, out var stored))
            {
                resolved[name] = stored;
            }
        }

        return resolved;
    }

    /// <summary>Splits a multi-line text box into entries, dropping blank lines.</summary>
    /// <param name="text">The text box contents.</param>
    /// <returns>One entry per non-blank line, trimmed.</returns>
    private static IReadOnlyList<string> SplitLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Normalises a blank text box to null.</summary>
    /// <param name="value">The text box contents.</param>
    /// <returns>The trimmed value, or null when blank.</returns>
    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
