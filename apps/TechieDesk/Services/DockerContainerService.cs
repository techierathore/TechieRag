using Docker.DotNet;
using Docker.DotNet.Models;

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
/// Information about a running Qdrant container.
/// </summary>
public record QdrantContainerInfo(
    string ContainerId,
    string ContainerName,
    string ImageName,
    ContainerStatus Status,
    int? HttpPort,
    int? GrpcPort);

/// <summary>
/// Service for managing Docker containers programmatically.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides ability to detect, create, start, stop, and manage
/// Docker containers, specifically for Qdrant vector database.</para>
/// <para><b>Code Flow:</b> Used by QdrantAdmin page to manage Qdrant container lifecycle.
/// Connects to Docker daemon via named pipe (Windows) or Unix socket (Linux).</para>
/// <para><b>Dependencies:</b> Docker.DotNet library for Docker API communication.</para>
/// </remarks>
public interface IDockerContainerService
{
    /// <summary>
    /// Checks if Docker daemon is accessible.
    /// </summary>
    /// <returns>True if Docker is available and responding.</returns>
    Task<bool> IsDockerAvailableAsync();

    /// <summary>
    /// Finds any running Qdrant container by image name.
    /// </summary>
    /// <returns>Information about the first Qdrant container found, or null if none.</returns>
    Task<QdrantContainerInfo?> FindQdrantContainerAsync();

    /// <summary>
    /// Lists all Qdrant containers (by image name).
    /// </summary>
    /// <returns>List of all Qdrant containers.</returns>
    Task<IReadOnlyList<QdrantContainerInfo>> ListQdrantContainersAsync();

    /// <summary>
    /// Checks if a container with the given name exists.
    /// </summary>
    /// <param name="containerName">Name of the container to check.</param>
    /// <returns>True if container exists.</returns>
    Task<bool> ContainerExistsAsync(string containerName);

    /// <summary>
    /// Gets the status of a container.
    /// </summary>
    /// <param name="containerName">Name of the container.</param>
    /// <returns>Current status of the container.</returns>
    Task<ContainerStatus> GetContainerStatusAsync(string containerName);

    /// <summary>
    /// Creates and starts a Qdrant container with default configuration.
    /// </summary>
    /// <param name="containerName">Name for the container (default: techierag-qdrant).</param>
    /// <param name="volumePath">Optional host path for persistent storage.</param>
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
    /// Removes a container.
    /// </summary>
    /// <param name="containerName">Name of the container to remove.</param>
    /// <param name="force">Force removal even if running.</param>
    Task RemoveContainerAsync(string containerName, bool force = false);

    /// <summary>
    /// Pulls the Qdrant image if not present.
    /// </summary>
    /// <param name="progress">Optional progress callback.</param>
    Task PullQdrantImageAsync(IProgress<string>? progress = null);
}

/// <summary>
/// Implementation of Docker container management service.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Manages Docker containers for Qdrant using Docker.DotNet library.</para>
/// <para><b>Code Flow:</b> Creates DockerClient on construction, methods call Docker API.</para>
/// </remarks>
public class DockerContainerService : IDockerContainerService, IDisposable
{
    private readonly DockerClient? client;
    private readonly ILogger<DockerContainerService> logger;
    private readonly bool isAvailable;

    /// <summary>
    /// Creates a new Docker container service instance.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public DockerContainerService(ILogger<DockerContainerService> logger)
    {
        this.logger = logger;

        try
        {
            // Connect to Docker daemon (Windows named pipe or Unix socket)
            var dockerUri = OperatingSystem.IsWindows()
                ? new Uri("npipe://./pipe/docker_engine")
                : new Uri("unix:///var/run/docker.sock");

            client = new DockerClientConfiguration(dockerUri).CreateClient();
            isAvailable = true;
            logger.LogInformation("Docker client initialized with endpoint: {Endpoint}", dockerUri);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to initialize Docker client - Docker may not be installed");
            isAvailable = false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsDockerAvailableAsync()
    {
        if (!isAvailable || client == null)
            return false;

        try
        {
            await client.System.PingAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Docker ping failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<QdrantContainerInfo?> FindQdrantContainerAsync()
    {
        var containers = await ListQdrantContainersAsync();
        return containers.FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QdrantContainerInfo>> ListQdrantContainersAsync()
    {
        if (client == null) return Array.Empty<QdrantContainerInfo>();

        try
        {
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true
            });

            var qdrantContainers = containers
                .Where(c => c.Image.Contains("qdrant", StringComparison.OrdinalIgnoreCase))
                .Select(c => new QdrantContainerInfo(
                    ContainerId: c.ID[..12],
                    ContainerName: c.Names.FirstOrDefault()?.TrimStart('/') ?? "unknown",
                    ImageName: c.Image,
                    Status: ParseContainerState(c.State),
                    HttpPort: GetHostPort(c.Ports, 6333),
                    GrpcPort: GetHostPort(c.Ports, 6334)
                ))
                .ToList();

            logger.LogInformation("Found {Count} Qdrant container(s)", qdrantContainers.Count);
            return qdrantContainers;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list Qdrant containers");
            return Array.Empty<QdrantContainerInfo>();
        }
    }

    private static ContainerStatus ParseContainerState(string state)
    {
        return state.ToLowerInvariant() switch
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
        var port = ports.FirstOrDefault(p => p.PrivatePort == privatePort);
        return port?.PublicPort > 0 ? (int)port.PublicPort : null;
    }

    /// <inheritdoc/>
    public async Task<bool> ContainerExistsAsync(string containerName)
    {
        if (client == null) return false;

        try
        {
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    { "name", new Dictionary<string, bool> { { containerName, true } } }
                }
            });

            return containers.Any(c => c.Names.Any(n => n.TrimStart('/') == containerName));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check container existence: {Container}", containerName);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<ContainerStatus> GetContainerStatusAsync(string containerName)
    {
        if (client == null) return ContainerStatus.NotFound;

        try
        {
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    { "name", new Dictionary<string, bool> { { containerName, true } } }
                }
            });

            var container = containers.FirstOrDefault(c => c.Names.Any(n => n.TrimStart('/') == containerName));

            if (container == null)
                return ContainerStatus.NotFound;

            return container.State.ToLowerInvariant() switch
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get container status: {Container}", containerName);
            return ContainerStatus.NotFound;
        }
    }

    /// <inheritdoc/>
    public async Task<string> CreateQdrantContainerAsync(string containerName = "techierag-qdrant", string? volumePath = null)
    {
        if (client == null)
            throw new InvalidOperationException("Docker client not available");

        logger.LogInformation("Creating Qdrant container: {Container}", containerName);

        // Pull image first
        await PullQdrantImageAsync(null);

        // Build bind mounts if volume path provided
        var binds = new List<string>();
        if (!string.IsNullOrEmpty(volumePath))
        {
            // Ensure directory exists
            Directory.CreateDirectory(volumePath);
            binds.Add($"{volumePath}:/qdrant/storage");
        }

        // Create container parameters
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

        var response = await client.Containers.CreateContainerAsync(createParams);
        logger.LogInformation("Created Qdrant container: {ContainerId}", response.ID);

        // Start the container
        await client.Containers.StartContainerAsync(response.ID, null);
        logger.LogInformation("Started Qdrant container: {ContainerId}", response.ID);

        return response.ID;
    }

    /// <inheritdoc/>
    public async Task StartContainerAsync(string containerName)
    {
        if (client == null)
            throw new InvalidOperationException("Docker client not available");

        var containerId = await GetContainerIdAsync(containerName);
        if (containerId == null)
            throw new InvalidOperationException($"Container not found: {containerName}");

        await client.Containers.StartContainerAsync(containerId, null);
        logger.LogInformation("Started container: {Container}", containerName);
    }

    /// <inheritdoc/>
    public async Task StopContainerAsync(string containerName)
    {
        if (client == null)
            throw new InvalidOperationException("Docker client not available");

        var containerId = await GetContainerIdAsync(containerName);
        if (containerId == null)
            throw new InvalidOperationException($"Container not found: {containerName}");

        await client.Containers.StopContainerAsync(containerId, new ContainerStopParameters { WaitBeforeKillSeconds = 10 });
        logger.LogInformation("Stopped container: {Container}", containerName);
    }

    /// <inheritdoc/>
    public async Task RemoveContainerAsync(string containerName, bool force = false)
    {
        if (client == null)
            throw new InvalidOperationException("Docker client not available");

        var containerId = await GetContainerIdAsync(containerName);
        if (containerId == null)
        {
            logger.LogWarning("Container not found for removal: {Container}", containerName);
            return;
        }

        await client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = force });
        logger.LogInformation("Removed container: {Container}", containerName);
    }

    /// <inheritdoc/>
    public async Task PullQdrantImageAsync(IProgress<string>? progress = null)
    {
        if (client == null)
            throw new InvalidOperationException("Docker client not available");

        logger.LogInformation("Pulling Qdrant image...");

        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = "qdrant/qdrant", Tag = "latest" },
            null,
            new Progress<JSONMessage>(msg =>
            {
                if (!string.IsNullOrEmpty(msg.Status))
                {
                    progress?.Report(msg.Status);
                    logger.LogDebug("Pull progress: {Status}", msg.Status);
                }
            }));

        logger.LogInformation("Qdrant image pulled successfully");
    }

    /// <summary>
    /// Gets the container ID by name.
    /// </summary>
    private async Task<string?> GetContainerIdAsync(string containerName)
    {
        if (client == null) return null;

        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                { "name", new Dictionary<string, bool> { { containerName, true } } }
            }
        });

        return containers.FirstOrDefault(c => c.Names.Any(n => n.TrimStart('/') == containerName))?.ID;
    }

    /// <summary>
    /// Disposes the Docker client.
    /// </summary>
    public void Dispose()
    {
        client?.Dispose();
        GC.SuppressFinalize(this);
    }
}
