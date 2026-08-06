using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Net.Http.Client;
using TechieDesk.Services.Data;

namespace TechieDesk.Services;

/// <summary>
/// Status of a Docker container.
/// </summary>
public enum ContainerStatus
{
    /// <summary>Container not found.</summary>
    NotFound,
    /// <summary>Container created but not started.</summary>
    Created,
    /// <summary>Container is running.</summary>
    Running,
    /// <summary>Container is paused.</summary>
    Paused,
    /// <summary>Container is restarting.</summary>
    Restarting,
    /// <summary>Container has exited.</summary>
    Exited,
    /// <summary>Container is dead.</summary>
    Dead
}

/// <summary>
/// Information about a Qdrant container on the connected daemon.
/// </summary>
/// <param name="ContainerId">Short container id.</param>
/// <param name="ContainerName">Container name without the leading slash.</param>
/// <param name="ImageName">Image the container was created from.</param>
/// <param name="Status">Parsed lifecycle status.</param>
/// <param name="StatusText">The daemon's own status text, for example <c>Up 3 days</c>.</param>
/// <param name="HttpPort">Published host port mapped to Qdrant's REST port, when published.</param>
/// <param name="GrpcPort">Published host port mapped to Qdrant's gRPC port, when published.</param>
public record QdrantContainerInfo(
    string ContainerId,
    string ContainerName,
    string ImageName,
    ContainerStatus Status,
    string StatusText,
    int? HttpPort,
    int? GrpcPort);

/// <summary>
/// The outcome of a Docker daemon connection test (REQ-FN-040 / REQ-UI-042).
/// </summary>
/// <remarks>
/// Failure is reported with the endpoint that was actually tried and the real reason it failed.
/// A connection problem must never surface as some unrelated application-level message.
/// </remarks>
public sealed record DockerDaemonTestResult
{
    /// <summary>Gets a value indicating whether the daemon answered.</summary>
    public required bool Success { get; init; }

    /// <summary>Gets the endpoint that was tested.</summary>
    public required DockerDaemonEndpoint Endpoint { get; init; }

    /// <summary>Gets the daemon's engine version, when it answered.</summary>
    public string? DaemonVersion { get; init; }

    /// <summary>Gets the daemon's API version, when it answered.</summary>
    public string? ApiVersion { get; init; }

    /// <summary>Gets the daemon host's operating system and architecture, when it answered.</summary>
    public string? HostOperatingSystem { get; init; }

    /// <summary>
    /// Gets the RESOURCE KEY and arguments for the reason the test failed. Always names the endpoint
    /// that was tried, and is <see langword="null"/> only when <see cref="Success"/> is
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REQ-UI-055 (BRD-91): this was an English sentence built in the service and rendered verbatim
    /// on <c>QdrantAdmin</c> — in the unreachable-daemon alert, in the container-lifecycle alert and
    /// in the failure toast. Neither razor counter can see a service, so it stayed English on a Hindi
    /// install while every localization test was green.
    /// </para>
    /// <para>
    /// <b>Some of the arguments are machine text and stay English by design.</b> A daemon's HTTP
    /// response body, a <see cref="System.Net.Sockets.SocketError"/> name and an exception's own
    /// message are relayed VERBATIM from Docker or from the runtime. They are not ours to translate
    /// and translating them would make them unsearchable; the sentence around them is what gets
    /// localized.
    /// </para>
    /// </remarks>
    public DockerEndpointProblem? Failure { get; init; }

    /// <summary>Gets the exception type name behind a failure, for the log and for support.</summary>
    public string? FailureKind { get; init; }

    /// <summary>Gets the moment the test ran.</summary>
    public required DateTimeOffset CheckedAt { get; init; }
}

/// <summary>
/// Service for managing Docker containers on a configurable Docker daemon.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Detect, create, start, stop, restart, pull and tail logs for Qdrant
/// containers on whichever Docker daemon the operator has pointed TechieDesk at — the local
/// socket, a machine on the LAN, or a remote host over TCP+TLS (REQ-FN-040 / BRD-134).</para>
/// <para><b>Code Flow:</b> The endpoint is loaded once from <see cref="IDockerDaemonSettingsStore"/>
/// and cached with the <see cref="DockerClient"/> built from it; applying a new endpoint disposes
/// the client so the next call rebuilds against the new daemon.</para>
/// <para><b>Security:</b> a Docker daemon endpoint is effectively root on the target host. TLS
/// verification is on by default and a plain <c>tcp://</c> endpoint always carries a warning.</para>
/// <para><b>Dependencies:</b> Docker.DotNet for the Docker API.</para>
/// </remarks>
public interface IDockerContainerService
{
    /// <summary>
    /// Gets the daemon endpoint currently in force. Falls back to the local socket until the
    /// persisted setting has been loaded; call <see cref="GetActiveEndpointAsync"/> to be sure.
    /// </summary>
    DockerDaemonEndpoint ActiveEndpoint { get; }

    /// <summary>
    /// Gets the result of the most recent connection test, or <see langword="null"/> when the
    /// daemon has not been contacted yet.
    /// </summary>
    DockerDaemonTestResult? LastTestResult { get; }

    /// <summary>
    /// Gets the resource key and arguments for why the daemon was last unreachable, or
    /// <see langword="null"/> when the last contact succeeded.
    /// </summary>
    DockerEndpointProblem? LastFailure { get; }

    /// <summary>
    /// Gets a value indicating whether a client certificate is configured for mutual TLS.
    /// REQ-FN-039 seam: the certificate's source moves to the OS credential store later.
    /// </summary>
    bool HasClientCertificate { get; }

    /// <summary>
    /// Resolves the active endpoint, loading the persisted setting on first use.
    /// </summary>
    /// <returns>The daemon endpoint TechieDesk is driving.</returns>
    Task<DockerDaemonEndpoint> GetActiveEndpointAsync();

    /// <summary>
    /// Points TechieDesk at a different Docker daemon and persists the choice.
    /// </summary>
    /// <param name="kind">Local socket, network host, or remote TCP+TLS.</param>
    /// <param name="address">Host or host:port for a network endpoint; ignored for a local socket.</param>
    /// <param name="verifyTls">Whether to verify the daemon's certificate chain. On by default.</param>
    /// <param name="persist">Whether to save the choice so it survives a restart.</param>
    /// <returns>The endpoint now in force.</returns>
    /// <exception cref="ArgumentException">The address could not be understood.</exception>
    Task<DockerDaemonEndpoint> ConfigureEndpointAsync(
        DockerDaemonEndpointKind kind,
        string? address,
        bool verifyTls = true,
        bool persist = true);

    /// <summary>
    /// Tests the active endpoint and reports honestly what happened.
    /// </summary>
    /// <returns>The test outcome, including the real failure reason when it failed.</returns>
    Task<DockerDaemonTestResult> TestConnectionAsync();

    /// <summary>
    /// Tests a candidate endpoint without changing the active configuration.
    /// </summary>
    /// <param name="endpoint">The endpoint to probe.</param>
    /// <returns>The test outcome, including the real failure reason when it failed.</returns>
    Task<DockerDaemonTestResult> TestConnectionAsync(DockerDaemonEndpoint endpoint);

    /// <summary>
    /// Checks if the configured Docker daemon is accessible.
    /// </summary>
    /// <returns>True if the daemon is available and responding.</returns>
    Task<bool> IsDockerAvailableAsync();

    /// <summary>
    /// Finds any Qdrant container on the connected daemon.
    /// </summary>
    /// <returns>Information about the first Qdrant container found, or null if none.</returns>
    Task<QdrantContainerInfo?> FindQdrantContainerAsync();

    /// <summary>
    /// Lists all Qdrant containers (by image name) on the connected daemon.
    /// </summary>
    /// <returns>List of all Qdrant containers.</returns>
    Task<IReadOnlyList<QdrantContainerInfo>> ListQdrantContainersAsync();

    /// <summary>
    /// Checks if a container with the given name exists on the connected daemon.
    /// </summary>
    /// <param name="containerName">Name of the container to check.</param>
    /// <returns>True if container exists.</returns>
    Task<bool> ContainerExistsAsync(string containerName);

    /// <summary>
    /// Gets the status of a container on the connected daemon.
    /// </summary>
    /// <param name="containerName">Name of the container.</param>
    /// <returns>Current status of the container.</returns>
    Task<ContainerStatus> GetContainerStatusAsync(string containerName);

    /// <summary>
    /// Creates and starts a Qdrant container with default configuration.
    /// </summary>
    /// <param name="containerName">Name for the container (default: techierag-qdrant).</param>
    /// <param name="volumePath">
    /// Optional path for persistent storage. The path is interpreted by the <i>daemon host</i>,
    /// not by the machine running TechieDesk.
    /// </param>
    /// <returns>The container ID.</returns>
    Task<string> CreateQdrantContainerAsync(string containerName = "techierag-qdrant", string? volumePath = null);

    /// <summary>
    /// Starts an existing container.
    /// </summary>
    /// <param name="containerName">Name of the container to start.</param>
    Task StartContainerAsync(string containerName);

    /// <summary>
    /// Stops a running container.
    /// </summary>
    /// <param name="containerName">Name of the container to stop.</param>
    Task StopContainerAsync(string containerName);

    /// <summary>
    /// Restarts a container on the connected daemon.
    /// </summary>
    /// <param name="containerName">Name of the container to restart.</param>
    Task RestartContainerAsync(string containerName);

    /// <summary>
    /// Reads the tail of a container's combined stdout/stderr log.
    /// </summary>
    /// <param name="containerName">Name of the container.</param>
    /// <param name="tailLines">How many trailing lines to read.</param>
    /// <returns>
    /// The log text, or an EMPTY string when the daemon produced none. REQ-UI-055: the service does
    /// not substitute a sentence of its own, because that sentence would be English on every install;
    /// the surface that renders the log decides what "nothing to show" reads like — see
    /// <see cref="DockerContainerService.NoLogOutputKey"/>.
    /// </returns>
    Task<string> GetContainerLogsAsync(string containerName, int tailLines = 200);

    /// <summary>
    /// Removes a container.
    /// </summary>
    /// <param name="containerName">Name of the container to remove.</param>
    /// <param name="force">Force removal even if running.</param>
    Task RemoveContainerAsync(string containerName, bool force = false);

    /// <summary>
    /// Pulls the Qdrant image onto the connected daemon.
    /// </summary>
    /// <param name="progress">Optional progress callback.</param>
    Task PullQdrantImageAsync(IProgress<string>? progress = null);
}

/// <summary>
/// Implementation of Docker container management against a configurable daemon endpoint.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Manages Qdrant containers on the configured Docker daemon using
/// Docker.DotNet (REQ-FN-040).</para>
/// <para><b>Code Flow:</b> Lazily loads the endpoint setting, builds a <see cref="DockerClient"/>
/// for it, and rebuilds that client whenever the endpoint changes.</para>
/// </remarks>
public sealed class DockerContainerService : IDockerContainerService, IDisposable
{
    /// <summary>How long a daemon call may take before it is reported as a timeout.</summary>
    public static readonly TimeSpan DaemonTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger<DockerContainerService> logger;
    private readonly IDockerDaemonSettingsStore settingsStore;
    private readonly SemaphoreSlim gate = new(1, 1);

    private DockerDaemonEndpoint activeEndpoint = DockerDaemonEndpoint.Local();
    private DockerDaemonSettings? activeSettings;
    private DockerClient? client;
    private bool loaded;
    private bool disposed;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <remarks>
    /// The settings store is composed here rather than injected because every dependency it needs
    /// is already registered by the host, so REQ-FN-040 lands without a new DI registration. A
    /// single public constructor also keeps container activation unambiguous.
    /// </remarks>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="instanceSettings">Repository holding the persisted daemon endpoint.</param>
    /// <param name="configuration">Application configuration supplying deployment defaults.</param>
    /// <param name="loggerFactory">Factory used to log from the composed settings store.</param>
    public DockerContainerService(
        ILogger<DockerContainerService> logger,
        IInstanceSettingRepository instanceSettings,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        this.logger = logger;
        settingsStore = new DockerDaemonSettingsStore(
            instanceSettings, configuration, loggerFactory.CreateLogger<DockerDaemonSettingsStore>());
    }

    /// <inheritdoc/>
    public DockerDaemonEndpoint ActiveEndpoint => activeEndpoint;

    /// <inheritdoc/>
    public DockerDaemonTestResult? LastTestResult { get; private set; }

    /// <inheritdoc/>
    public DockerEndpointProblem? LastFailure =>
        LastTestResult is { Success: false } failure ? failure.Failure : null;

    /// <inheritdoc/>
    public bool HasClientCertificate => !string.IsNullOrWhiteSpace(activeSettings?.ClientCertificatePath);

    /// <inheritdoc/>
    public async Task<DockerDaemonEndpoint> GetActiveEndpointAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        return activeEndpoint;
    }

    /// <inheritdoc/>
    public async Task<DockerDaemonEndpoint> ConfigureEndpointAsync(
        DockerDaemonEndpointKind kind,
        string? address,
        bool verifyTls = true,
        bool persist = true)
    {
        var endpoint = DockerDaemonEndpoint.FromKind(kind, address, verifyTls);

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            activeEndpoint = endpoint;
            activeSettings = (activeSettings ?? await settingsStore.LoadAsync().ConfigureAwait(false)) with
            {
                Endpoint = endpoint.Display,
                VerifyTls = endpoint.VerifyTls
            };
            loaded = true;
            DisposeClient();
        }
        finally
        {
            gate.Release();
        }

        if (endpoint.HasSecurityWarning)
        {
            logger.LogWarning(
                "Docker daemon endpoint {Endpoint} configured with a security warning: {Warning}",
                endpoint.Display, endpoint.SecurityWarningKey);
        }
        else
        {
            logger.LogInformation("Docker daemon endpoint configured: {Endpoint} ({Kind})",
                endpoint.Display, endpoint.Kind);
        }

        if (persist)
        {
            try
            {
                await settingsStore.SaveAsync(activeSettings!).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Honest about the half-success: the endpoint IS in force for this process, it
                // just will not survive a restart. Saying nothing here would be a lie by omission.
                logger.LogWarning(ex,
                    "Docker daemon endpoint {Endpoint} is active but could not be persisted; it will " +
                    "revert on restart", endpoint.Display);
            }
        }

        LastTestResult = null;
        return endpoint;
    }

    /// <inheritdoc/>
    public async Task<DockerDaemonTestResult> TestConnectionAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        var result = await ProbeAsync(activeEndpoint, activeSettings).ConfigureAwait(false);
        LastTestResult = result;
        return result;
    }

    /// <inheritdoc/>
    public async Task<DockerDaemonTestResult> TestConnectionAsync(DockerDaemonEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        await EnsureLoadedAsync().ConfigureAwait(false);
        return await ProbeAsync(endpoint, activeSettings).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> IsDockerAvailableAsync()
    {
        var result = await TestConnectionAsync().ConfigureAwait(false);
        return result.Success;
    }

    /// <summary>
    /// Probes an endpoint and turns whatever happened into an honest, endpoint-named result.
    /// </summary>
    private async Task<DockerDaemonTestResult> ProbeAsync(
        DockerDaemonEndpoint endpoint,
        DockerDaemonSettings? settings)
    {
        DockerClient? probe = null;
        try
        {
            probe = CreateClient(endpoint, settings);
            var version = await probe.System.GetVersionAsync().ConfigureAwait(false);

            logger.LogInformation(
                "Docker daemon at {Endpoint} answered: engine {Version}, API {ApiVersion}, host {Os}/{Arch}",
                endpoint.Display, version.Version, version.APIVersion, version.Os, version.Arch);

            return new DockerDaemonTestResult
            {
                Success = true,
                Endpoint = endpoint,
                DaemonVersion = version.Version,
                ApiVersion = version.APIVersion,
                HostOperatingSystem = string.Join(" · ",
                    new[] { version.Os, version.Arch, version.KernelVersion }
                        .Where(part => !string.IsNullOrWhiteSpace(part))),
                CheckedAt = DateTimeOffset.Now
            };
        }
        catch (Exception ex)
        {
            var reason = DescribeFailure(endpoint, ex);
            logger.LogWarning(ex, "Docker daemon at {Endpoint} is unreachable: {Reason}",
                endpoint.Display, reason);

            return new DockerDaemonTestResult
            {
                Success = false,
                Endpoint = endpoint,
                Failure = reason,
                FailureKind = ex.GetType().Name,
                CheckedAt = DateTimeOffset.Now
            };
        }
        finally
        {
            probe?.Dispose();
        }
    }

    /// <summary>Resource key: the daemon answered but refused the request.</summary>
    public const string RefusedRequestKey = "QdrantDaemonRefusedRequest";

    /// <summary>Resource key: the TLS handshake with the daemon failed.</summary>
    public const string TlsHandshakeFailedKey = "QdrantDaemonTlsHandshakeFailed";

    /// <summary>Resource key: nothing is listening at the endpoint.</summary>
    public const string ConnectionRefusedKey = "QdrantDaemonConnectionRefused";

    /// <summary>Resource key: the endpoint's host name did not resolve.</summary>
    public const string HostNotResolvedKey = "QdrantDaemonHostNotResolved";

    /// <summary>Resource key: the connection attempt itself timed out.</summary>
    public const string ConnectTimedOutKey = "QdrantDaemonConnectTimedOut";

    /// <summary>Resource key: the endpoint is not routable from this machine.</summary>
    public const string HostUnreachableKey = "QdrantDaemonHostUnreachable";

    /// <summary>Resource key: some other socket error, relayed with its code.</summary>
    public const string SocketFailureKey = "QdrantDaemonSocketFailure";

    /// <summary>Resource key: the endpoint connected but the daemon never answered.</summary>
    public const string NoAnswerKey = "QdrantDaemonNoAnswer";

    /// <summary>Resource key: the Windows named pipe is absent.</summary>
    public const string NamedPipeMissingKey = "QdrantDaemonNamedPipeMissing";

    /// <summary>Resource key: the unix domain socket is absent.</summary>
    public const string LocalSocketMissingKey = "QdrantDaemonLocalSocketMissing";

    /// <summary>Resource key: an unclassified transport failure, relayed with its exception type.</summary>
    public const string UnexpectedFailureKey = "QdrantDaemonUnexpectedFailure";

    /// <summary>Resource key: the daemon returned no log output for a container.</summary>
    public const string NoLogOutputKey = "QdrantContainerNoLogOutput";

    /// <summary>
    /// Turns a Docker transport failure into a refusal an operator can act on. It always names
    /// the endpoint that was tried, so a daemon outage can never be mistaken for a different fault.
    /// </summary>
    /// <param name="endpoint">The endpoint that was being contacted.</param>
    /// <param name="error">The exception that came back.</param>
    /// <returns>A resource key plus the arguments its placeholders take.</returns>
    /// <remarks>
    /// <para>
    /// REQ-UI-055: a KEY, resolved by whatever renders it. The first argument is always
    /// <see cref="DockerDaemonEndpoint.Display"/>, which is wire vocabulary and identical in every
    /// culture.
    /// </para>
    /// <para>
    /// <b>Machine-facing arguments.</b> <c>api.ResponseBody</c>, <c>socket.SocketErrorCode</c>,
    /// <c>socket.Message</c>, <c>tls.Message</c> and <c>error.Message</c> come from the Docker daemon
    /// or the .NET runtime, not from TechieDesk. They stay in whatever language their source emits —
    /// translating a daemon's own error text would invent a message the daemon never produced and
    /// would make it impossible to search for.
    /// </para>
    /// </remarks>
    public static DockerEndpointProblem DescribeFailure(DockerDaemonEndpoint endpoint, Exception error)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(error);

        var target = endpoint.Display;

        if (error is DockerApiException api)
        {
            return new DockerEndpointProblem(
                RefusedRequestKey,
                [
                    target,
                    ((int)api.StatusCode).ToString(CultureInfo.InvariantCulture),
                    api.StatusCode.ToString(),
                    api.ResponseBody ?? string.Empty
                ]);
        }

        if (FindInner<AuthenticationException>(error) is { } tls)
        {
            return new DockerEndpointProblem(TlsHandshakeFailedKey, [target, tls.Message]);
        }

        if (FindInner<SocketException>(error) is { } socket)
        {
            return socket.SocketErrorCode switch
            {
                SocketError.ConnectionRefused =>
                    new DockerEndpointProblem(ConnectionRefusedKey, [target]),
                SocketError.HostNotFound or SocketError.NoData =>
                    new DockerEndpointProblem(HostNotResolvedKey, [target]),
                SocketError.TimedOut =>
                    new DockerEndpointProblem(ConnectTimedOutKey, [target]),
                SocketError.NetworkUnreachable or SocketError.HostUnreachable =>
                    new DockerEndpointProblem(HostUnreachableKey, [target]),
                SocketError.AddressNotAvailable when endpoint.Kind == DockerDaemonEndpointKind.LocalSocket =>
                    DescribeMissingLocalSocket(endpoint),
                _ => new DockerEndpointProblem(
                    SocketFailureKey, [target, socket.SocketErrorCode.ToString(), socket.Message])
            };
        }

        if (error is TimeoutException || error is TaskCanceledException || error is OperationCanceledException)
        {
            return new DockerEndpointProblem(
                NoAnswerKey,
                [DaemonTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture), target]);
        }

        if (endpoint.Kind == DockerDaemonEndpointKind.LocalSocket &&
            (FindInner<FileNotFoundException>(error) is not null ||
             FindInner<DirectoryNotFoundException>(error) is not null))
        {
            return DescribeMissingLocalSocket(endpoint);
        }

        if (endpoint.Kind == DockerDaemonEndpointKind.LocalSocket && !LocalSocketExists(endpoint))
        {
            return DescribeMissingLocalSocket(endpoint);
        }

        return new DockerEndpointProblem(
            UnexpectedFailureKey, [target, error.GetType().Name, error.Message]);
    }

    private static DockerEndpointProblem DescribeMissingLocalSocket(DockerDaemonEndpoint endpoint)
    {
        var path = LocalSocketPath(endpoint);
        return new DockerEndpointProblem(
            OperatingSystem.IsWindows() ? NamedPipeMissingKey : LocalSocketMissingKey,
            [path]);
    }

    private static string LocalSocketPath(DockerDaemonEndpoint endpoint) =>
        endpoint.Uri.Scheme.Equals("npipe", StringComparison.OrdinalIgnoreCase)
            ? endpoint.Uri.OriginalString
            : endpoint.Uri.LocalPath;

    private static bool LocalSocketExists(DockerDaemonEndpoint endpoint)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            return File.Exists(endpoint.Uri.LocalPath);
        }
        catch
        {
            return true;
        }
    }

    private static TException? FindInner<TException>(Exception error) where TException : Exception
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }

    private async Task EnsureLoadedAsync()
    {
        if (loaded)
        {
            return;
        }

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (loaded)
            {
                return;
            }

            var settings = await settingsStore.LoadAsync().ConfigureAwait(false);
            activeSettings = settings;

            if (DockerDaemonEndpoint.TryParse(settings.Endpoint, settings.VerifyTls, out var endpoint, out var problem))
            {
                activeEndpoint = endpoint;
            }
            else
            {
                // Refuse to guess. Fall back to the local socket but say loudly that the
                // configured value was rejected, so the UI never shows a daemon nobody chose.
                activeEndpoint = DockerDaemonEndpoint.Local();
                logger.LogError("Configured Docker daemon endpoint was rejected: {Error}", problem);
            }

            loaded = true;
            logger.LogInformation("Docker daemon endpoint in force: {Endpoint} ({Kind}, TLS {Tls})",
                activeEndpoint.Display, activeEndpoint.Kind,
                activeEndpoint.UsesTls ? (activeEndpoint.VerifyTls ? "verified" : "UNVERIFIED") : "off");

            if (activeEndpoint.HasSecurityWarning)
            {
                logger.LogWarning("Docker daemon endpoint {Endpoint}: {Warning}",
                    activeEndpoint.Display, activeEndpoint.SecurityWarningKey);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Gets (creating if needed) the client bound to the active endpoint.
    /// </summary>
    private async Task<DockerClient> GetClientAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return client ??= CreateClient(activeEndpoint, activeSettings);
        }
        finally
        {
            gate.Release();
        }
    }

    private DockerClient CreateClient(DockerDaemonEndpoint endpoint, DockerDaemonSettings? settings)
    {
        var credentials = new DockerDaemonCredentials(endpoint, LoadClientCertificate(endpoint, settings), logger);
        var configuration = new DockerClientConfiguration(endpoint.ClientUri, credentials, DaemonTimeout);
        return configuration.CreateClient();
    }

    /// <summary>
    /// REQ-FN-039 seam. The client certificate is resolved through the daemon settings
    /// abstraction only; when the OS credential store lands it replaces the source, not this code.
    /// </summary>
    private X509Certificate2? LoadClientCertificate(DockerDaemonEndpoint endpoint, DockerDaemonSettings? settings)
    {
        var path = settings?.ClientCertificatePath;
        if (string.IsNullOrWhiteSpace(path) || !endpoint.UsesTls)
        {
            return null;
        }

        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(path, settings?.ClientCertificatePassword);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Docker client certificate at {Path} could not be loaded; the connection to {Endpoint} " +
                "will be attempted without it and will fail if the daemon requires mutual TLS",
                path, endpoint.Display);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<QdrantContainerInfo?> FindQdrantContainerAsync()
    {
        var containers = await ListQdrantContainersAsync().ConfigureAwait(false);
        return containers.FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QdrantContainerInfo>> ListQdrantContainersAsync()
    {
        try
        {
            var docker = await GetClientAsync().ConfigureAwait(false);
            var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true
            }).ConfigureAwait(false);

            var qdrantContainers = containers
                .Where(c => c.Image.Contains("qdrant", StringComparison.OrdinalIgnoreCase))
                .Select(c => new QdrantContainerInfo(
                    ContainerId: c.ID.Length > 12 ? c.ID[..12] : c.ID,
                    ContainerName: c.Names.FirstOrDefault()?.TrimStart('/') ?? "unknown",
                    ImageName: c.Image,
                    Status: ParseContainerState(c.State),
                    StatusText: c.Status ?? string.Empty,
                    HttpPort: GetHostPort(c.Ports, 6333),
                    GrpcPort: GetHostPort(c.Ports, 6334)
                ))
                .ToList();

            logger.LogInformation("Found {Count} Qdrant container(s) on {Endpoint}",
                qdrantContainers.Count, activeEndpoint.Display);
            return qdrantContainers;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list Qdrant containers on {Endpoint}", activeEndpoint.Display);
            return Array.Empty<QdrantContainerInfo>();
        }
    }

    private static ContainerStatus ParseContainerState(string state)
    {
        return state?.ToLowerInvariant() switch
        {
            "created" => ContainerStatus.Created,
            "running" => ContainerStatus.Running,
            "paused" => ContainerStatus.Paused,
            "restarting" => ContainerStatus.Restarting,
            "exited" => ContainerStatus.Exited,
            "dead" => ContainerStatus.Dead,
            _ => ContainerStatus.NotFound
        };
    }

    private static int? GetHostPort(IList<Port> ports, ushort privatePort)
    {
        var port = ports?.FirstOrDefault(p => p.PrivatePort == privatePort);
        return port?.PublicPort > 0 ? (int)port.PublicPort : null;
    }

    /// <inheritdoc/>
    public async Task<bool> ContainerExistsAsync(string containerName)
    {
        try
        {
            var container = await FindContainerAsync(containerName).ConfigureAwait(false);
            return container is not null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check container existence: {Container} on {Endpoint}",
                containerName, activeEndpoint.Display);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<ContainerStatus> GetContainerStatusAsync(string containerName)
    {
        try
        {
            var container = await FindContainerAsync(containerName).ConfigureAwait(false);
            return container is null ? ContainerStatus.NotFound : ParseContainerState(container.State);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get container status: {Container} on {Endpoint}",
                containerName, activeEndpoint.Display);
            return ContainerStatus.NotFound;
        }
    }

    /// <inheritdoc/>
    public async Task<string> CreateQdrantContainerAsync(
        string containerName = "techierag-qdrant",
        string? volumePath = null)
    {
        var docker = await GetClientAsync().ConfigureAwait(false);
        logger.LogInformation("Creating Qdrant container {Container} on {Endpoint}",
            containerName, activeEndpoint.Display);

        await PullQdrantImageAsync(null).ConfigureAwait(false);

        // The bind path is resolved by the DAEMON host, which may not be this machine — so we
        // must not create the directory locally when the daemon is remote.
        var binds = new List<string>();
        if (!string.IsNullOrEmpty(volumePath))
        {
            if (activeEndpoint.Kind == DockerDaemonEndpointKind.LocalSocket)
            {
                Directory.CreateDirectory(volumePath);
            }
            else
            {
                logger.LogInformation(
                    "Volume path {VolumePath} will be resolved on the remote daemon host {Endpoint}, " +
                    "not on this machine", volumePath, activeEndpoint.Display);
            }

            binds.Add($"{volumePath}:/qdrant/storage");
        }

        var createParams = new CreateContainerParameters
        {
            Image = "qdrant/qdrant:latest",
            Name = containerName,
            HostConfig = new HostConfig
            {
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    { "6333/tcp", new List<PortBinding> { new() { HostPort = "6333" } } },
                    { "6334/tcp", new List<PortBinding> { new() { HostPort = "6334" } } }
                },
                Binds = binds.Count > 0 ? binds : null,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
            },
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                { "6333/tcp", default },
                { "6334/tcp", default }
            }
        };

        var response = await docker.Containers.CreateContainerAsync(createParams).ConfigureAwait(false);
        logger.LogInformation("Created Qdrant container {ContainerId} on {Endpoint}",
            response.ID, activeEndpoint.Display);

        await docker.Containers.StartContainerAsync(response.ID, null).ConfigureAwait(false);
        logger.LogInformation("Started Qdrant container {ContainerId}", response.ID);

        return response.ID;
    }

    /// <inheritdoc/>
    public async Task StartContainerAsync(string containerName)
    {
        var docker = await GetClientAsync().ConfigureAwait(false);
        var containerId = await RequireContainerIdAsync(containerName).ConfigureAwait(false);
        await docker.Containers.StartContainerAsync(containerId, null).ConfigureAwait(false);
        logger.LogInformation("Started container {Container} on {Endpoint}", containerName, activeEndpoint.Display);
    }

    /// <inheritdoc/>
    public async Task StopContainerAsync(string containerName)
    {
        var docker = await GetClientAsync().ConfigureAwait(false);
        var containerId = await RequireContainerIdAsync(containerName).ConfigureAwait(false);
        await docker.Containers.StopContainerAsync(containerId,
            new ContainerStopParameters { WaitBeforeKillSeconds = 10 }).ConfigureAwait(false);
        logger.LogInformation("Stopped container {Container} on {Endpoint}", containerName, activeEndpoint.Display);
    }

    /// <inheritdoc/>
    public async Task RestartContainerAsync(string containerName)
    {
        var docker = await GetClientAsync().ConfigureAwait(false);
        var containerId = await RequireContainerIdAsync(containerName).ConfigureAwait(false);
        await docker.Containers.RestartContainerAsync(containerId,
            new ContainerRestartParameters { WaitBeforeKillSeconds = 10 }).ConfigureAwait(false);
        logger.LogInformation("Restarted container {Container} on {Endpoint}", containerName, activeEndpoint.Display);
    }

    /// <inheritdoc/>
    public async Task<string> GetContainerLogsAsync(string containerName, int tailLines = 200)
    {
        var docker = await GetClientAsync().ConfigureAwait(false);
        var containerId = await RequireContainerIdAsync(containerName).ConfigureAwait(false);

        var inspected = await docker.Containers.InspectContainerAsync(containerId).ConfigureAwait(false);
        var usesTty = inspected.Config?.Tty ?? false;

        using var stream = await docker.Containers.GetContainerLogsAsync(
            containerId,
            usesTty,
            new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Tail = tailLines.ToString()
            }).ConfigureAwait(false);

        var (stdout, stderr) = await stream.ReadOutputToEndAsync(CancellationToken.None).ConfigureAwait(false);
        var combined = string.Concat(stdout, stderr);

        logger.LogInformation("Read {Length} characters of log from {Container} on {Endpoint}",
            combined.Length, containerName, activeEndpoint.Display);

        return combined;
    }

    /// <inheritdoc/>
    public async Task RemoveContainerAsync(string containerName, bool force = false)
    {
        var docker = await GetClientAsync().ConfigureAwait(false);
        var container = await FindContainerAsync(containerName).ConfigureAwait(false);
        if (container is null)
        {
            logger.LogWarning("Container not found for removal: {Container} on {Endpoint}",
                containerName, activeEndpoint.Display);
            return;
        }

        await docker.Containers.RemoveContainerAsync(container.ID,
            new ContainerRemoveParameters { Force = force }).ConfigureAwait(false);
        logger.LogInformation("Removed container {Container} on {Endpoint}", containerName, activeEndpoint.Display);
    }

    /// <inheritdoc/>
    public async Task PullQdrantImageAsync(IProgress<string>? progress = null)
    {
        var docker = await GetClientAsync().ConfigureAwait(false);
        logger.LogInformation("Pulling qdrant/qdrant:latest onto {Endpoint}", activeEndpoint.Display);

        await docker.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = "qdrant/qdrant", Tag = "latest" },
            null,
            new Progress<JSONMessage>(msg =>
            {
                if (!string.IsNullOrEmpty(msg.Status))
                {
                    progress?.Report(msg.Status);
                    logger.LogDebug("Pull progress: {Status}", msg.Status);
                }
            })).ConfigureAwait(false);

        logger.LogInformation("Qdrant image pulled onto {Endpoint}", activeEndpoint.Display);
    }

    private async Task<ContainerListResponse?> FindContainerAsync(string containerName)
    {
        var docker = await GetClientAsync().ConfigureAwait(false);
        var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                { "name", new Dictionary<string, bool> { { containerName, true } } }
            }
        }).ConfigureAwait(false);

        return containers.FirstOrDefault(c => c.Names.Any(n => n.TrimStart('/') == containerName));
    }

    private async Task<string> RequireContainerIdAsync(string containerName)
    {
        var container = await FindContainerAsync(containerName).ConfigureAwait(false);
        return container?.ID ?? throw new InvalidOperationException(
            $"Container '{containerName}' does not exist on the Docker daemon at {activeEndpoint.Display}.");
    }

    private void DisposeClient()
    {
        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// Disposes the Docker client bound to the active endpoint.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DisposeClient();
        gate.Dispose();
    }
}

/// <summary>
/// Docker.DotNet credentials describing the security posture of a configured daemon endpoint
/// (REQ-FN-040).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Docker.DotNet only keeps a TCP connection encrypted when the credentials
/// report TLS, so this type is what makes <c>tcps://</c> actually mean TLS.</para>
/// <para><b>Security:</b> the server certificate is verified by default — the validation callback
/// is only overridden when the operator has explicitly opted out, and that opt-out is logged as a
/// warning every time a client is built. If the opt-out cannot be applied the connection stays
/// <i>verified</i>: failing closed is the only safe direction here.</para>
/// </remarks>
internal sealed class DockerDaemonCredentials : Credentials
{
    private readonly DockerDaemonEndpoint endpoint;
    private readonly X509Certificate2? clientCertificate;
    private readonly ILogger logger;

    /// <summary>
    /// Creates credentials for an endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint these credentials describe.</param>
    /// <param name="clientCertificate">Optional client certificate for mutual TLS.</param>
    /// <param name="logger">Logger used to report security-relevant decisions.</param>
    public DockerDaemonCredentials(
        DockerDaemonEndpoint endpoint,
        X509Certificate2? clientCertificate,
        ILogger logger)
    {
        this.endpoint = endpoint;
        this.clientCertificate = clientCertificate;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public override bool IsTlsCredentials() => endpoint.UsesTls;

    /// <inheritdoc/>
    public override HttpMessageHandler GetHandler(HttpMessageHandler innerHandler)
    {
        if (innerHandler is not ManagedHandler managed)
        {
            if (!endpoint.VerifyTls)
            {
                logger.LogWarning(
                    "TLS verification could not be relaxed for {Endpoint}; the connection stays verified",
                    endpoint.Display);
            }

            return innerHandler;
        }

        if (clientCertificate is not null)
        {
            managed.ClientCertificates = new X509CertificateCollection { clientCertificate };
        }

        if (endpoint.UsesTls && !endpoint.VerifyTls)
        {
            logger.LogWarning(
                "TLS certificate verification is DISABLED for the Docker daemon at {Endpoint}. " +
                "The channel is encrypted but the daemon's identity is not proven.",
                endpoint.Display);
            managed.ServerCertificateValidationCallback =
                static (_, _, _, _) => true;
        }

        return innerHandler;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        clientCertificate?.Dispose();
        base.Dispose();
    }
}
