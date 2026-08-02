using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Mcp;

/// <summary>
/// Reaches an MCP server that runs as a local child process, speaking newline-delimited JSON-RPC
/// over its stdin and stdout (REQ-RAG-038).
/// </summary>
/// <remarks>
/// <para><b>This is the dangerous transport, and it is built to admit that.</b> It starts a process
/// on the user's machine. Every mitigation is structural rather than advisory:</para>
/// <list type="bullet">
/// <item><description><c>UseShellExecute</c> is false and arguments are supplied through
/// <see cref="ProcessStartInfo.ArgumentList"/>, so no shell ever parses anything — a value
/// containing <c>;</c>, <c>&amp;&amp;</c> or a quote is one literal argument, not a second command.</description></item>
/// <item><description>The executable must already have passed
/// <see cref="McpServerConfig.Validate(McpTrustPolicy)"/>, which demands a fully-qualified path
/// inside the host's allow-list and an explicit
/// <see cref="McpTrustPolicy.AllowLocalProcessLaunch"/>. This class re-checks both on start rather
/// than trusting that a caller did.</description></item>
/// <item><description>The path is confirmed to exist before launch, so a missing server is a clear
/// error instead of an OS-dependent fallback search.</description></item>
/// <item><description>The child's stdout is read but never executed, and its stderr is surfaced
/// only at debug level.</description></item>
/// </list>
/// <para><b>What is still trusted:</b> the executable itself. Once launched it runs with the host
/// user's privileges — the library cannot sandbox it. That is exactly why launching is opt-in and
/// path-restricted: the security decision belongs to whoever chose the binary.</para>
/// <para><b>Credentials:</b> <see cref="McpServerConfig.EnvironmentVariables"/> values are passed to
/// the child and never logged.</para>
/// </remarks>
public sealed class StdioMcpTransport : IMcpTransport
{
    private readonly McpServerConfig config;
    private readonly McpTrustPolicy policy;
    private readonly ILogger<StdioMcpTransport> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? process;
    private long nextId;

    /// <inheritdoc/>
    public string ServerName => config.Name;

    /// <summary>
    /// Creates a stdio transport for a validated server configuration.
    /// </summary>
    /// <param name="config">The server configuration.</param>
    /// <param name="policy">The host's trust policy; the configuration is validated against it here.</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <exception cref="McpConfigurationException">Thrown when the configuration is not permitted by the policy.</exception>
    public StdioMcpTransport(McpServerConfig config, McpTrustPolicy policy, ILogger<StdioMcpTransport>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(policy);

        config.Validate(policy);

        this.config = config;
        this.policy = policy;
        this.logger = logger ?? NullLogger<StdioMcpTransport>.Instance;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (process is not null) return Task.CompletedTask;

        // Re-assert the policy at the moment of launch. Validation happened in the constructor, but
        // this is the line that actually creates a process, and it must not depend on an earlier
        // check having been reached.
        if (!policy.AllowLocalProcessLaunch)
        {
            throw new McpException(ServerName, $"Launching MCP server '{ServerName}' is not permitted by the host's trust policy.");
        }

        var command = config.Command
            ?? throw new McpException(ServerName, $"MCP server '{ServerName}' has no command configured.");

        if (!File.Exists(command))
        {
            throw new McpException(ServerName, $"MCP server '{ServerName}' executable was not found at the configured path.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in config.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in config.EnvironmentVariables)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        if (config.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = config.WorkingDirectory;
        }

        logger.LogInformation("Starting MCP server {Server}", config.Describe());

        try
        {
            process = Process.Start(startInfo)
                ?? throw new McpException(ServerName, $"MCP server '{ServerName}' could not be started.");
        }
        catch (Exception ex) when (ex is not McpException)
        {
            throw new McpException(ServerName, $"MCP server '{ServerName}' could not be started: {ex.Message}", null, ex);
        }

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                logger.LogDebug("MCP server {ServerName} stderr: {Line}", ServerName, args.Data);
            }
        };
        process.BeginErrorReadLine();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);

        var running = RequireProcess();
        var id = Interlocked.Increment(ref nextId);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteLineAsync(running, JsonRpc.SerializeRequest(id, method, parameters), cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

            var response = await ReadResponseAsync(running, id, timeout.Token).ConfigureAwait(false);
            return JsonRpc.ReadResult(ServerName, response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new McpException(ServerName, $"MCP server '{ServerName}' did not answer '{method}' within {config.TimeoutSeconds}s.");
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);

        var running = RequireProcess();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteLineAsync(running, JsonRpc.SerializeNotification(method, parameters), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        var running = process;
        process = null;

        if (running is not null)
        {
            try
            {
                if (!running.HasExited)
                {
                    // Closing stdin is the protocol-level way to ask an MCP server to shut down; kill
                    // only if it does not take the hint.
                    running.StandardInput.Close();
                    if (!running.WaitForExit(2000)) running.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "MCP server {ServerName} did not shut down cleanly", ServerName);
            }

            running.Dispose();
        }

        gate.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private Process RequireProcess()
    {
        var running = process;
        if (running is null)
        {
            throw new McpException(ServerName, $"MCP server '{ServerName}' has not been started.");
        }

        if (running.HasExited)
        {
            throw new McpException(ServerName, $"MCP server '{ServerName}' has exited (code {running.ExitCode}).");
        }

        return running;
    }

    private static async Task WriteLineAsync(Process running, string payload, CancellationToken cancellationToken)
    {
        await running.StandardInput.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
        await running.StandardInput.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
        await running.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement> ReadResponseAsync(Process running, long id, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await running.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new McpException(ServerName, $"MCP server '{ServerName}' closed its output stream before answering.");
            }

            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonElement message;
            try
            {
                message = JsonRpc.ParseDetached(line);
            }
            catch (JsonException)
            {
                // A well-behaved server writes only JSON-RPC to stdout, but some print banners.
                // Skipping non-JSON lines is more useful than failing the whole session over one.
                logger.LogDebug("MCP server {ServerName} wrote a non-JSON line to stdout", ServerName);
                continue;
            }

            if (JsonRpc.IsResponseTo(message, id)) return message;
        }
    }
}
