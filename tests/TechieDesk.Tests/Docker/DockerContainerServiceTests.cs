using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services;
using TechieDesk.Services.Data;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Docker;

/// <summary>
/// In-memory stand-in for the Dapper instance-setting repository, so the daemon endpoint setting
/// can be exercised without a database.
/// </summary>
internal sealed class FakeInstanceSettingRepository : IInstanceSettingRepository
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    /// <summary>Gets a value indicating whether reads and writes should throw.</summary>
    public bool Broken { get; set; }

    /// <inheritdoc/>
    public Task<string?> GetAsync(string settingKey)
    {
        if (Broken)
        {
            throw new InvalidOperationException("settings store unavailable");
        }

        return Task.FromResult(values.TryGetValue(settingKey, out var value) ? value : null);
    }

    /// <inheritdoc/>
    public string? Get(string settingKey)
    {
        if (Broken)
        {
            throw new InvalidOperationException("settings store unavailable");
        }

        return values.TryGetValue(settingKey, out var value) ? value : null;
    }

    /// <inheritdoc/>
    public Task SetAsync(string settingKey, string settingValue)
    {
        if (Broken)
        {
            throw new InvalidOperationException("settings store unavailable");
        }

        values[settingKey] = settingValue;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<InstanceSetting>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<InstanceSetting>>(Array.Empty<InstanceSetting>());
}

/// <summary>
/// Guards REQ-FN-040: the daemon endpoint is a persisted setting, and a daemon that cannot be
/// reached is reported honestly rather than as some unrelated application-level condition.
/// </summary>
public class DockerContainerServiceTests
{
    private static DockerContainerService CreateService(
        IInstanceSettingRepository repository,
        params (string Key, string Value)[] configuration)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configuration.Select(pair =>
                new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

        return new DockerContainerService(
            NullLogger<DockerContainerService>.Instance,
            repository,
            config,
            NullLoggerFactory.Instance);
    }

    /// <summary>
    /// With nothing configured the service drives this machine's daemon, so the zero-configuration
    /// case still works after the endpoint became a setting.
    /// </summary>
    [Fact]
    public async Task DefaultsToTheLocalSocket()
    {
        using var service = CreateService(new FakeInstanceSettingRepository());

        var endpoint = await service.GetActiveEndpointAsync();

        Assert.Equal(DockerDaemonEndpointKind.LocalSocket, endpoint.Kind);
        Assert.Equal(DockerDaemonEndpoint.Local().Display, endpoint.Display);
    }

    /// <summary>
    /// A deployment can pin the daemon through configuration, and TLS verification stays on
    /// because nothing turned it off.
    /// </summary>
    [Fact]
    public async Task ConfiguredRemoteEndpointIsUsedWithVerificationOn()
    {
        using var service = CreateService(
            new FakeInstanceSettingRepository(),
            (DockerDaemonSettingsStore.EndpointConfigKey, "tcps://qdrant-host.lan:2376"));

        var endpoint = await service.GetActiveEndpointAsync();

        Assert.Equal(DockerDaemonEndpointKind.RemoteTls, endpoint.Kind);
        Assert.True(endpoint.UsesTls);
        Assert.True(endpoint.VerifyTls);
        Assert.False(endpoint.HasSecurityWarning);
    }

    /// <summary>
    /// The operator's choice on the screen is persisted, so the app comes back pointing at the
    /// same daemon rather than silently reverting to the local socket.
    /// </summary>
    [Fact]
    public async Task ConfiguredEndpointIsPersistedAndReloaded()
    {
        var repository = new FakeInstanceSettingRepository();
        using (var service = CreateService(repository))
        {
            await service.ConfigureEndpointAsync(DockerDaemonEndpointKind.RemoteTls, "qdrant-host.lan:2376");
        }

        Assert.Equal("tcps://qdrant-host.lan:2376",
            await repository.GetAsync(DockerDaemonSettingsStore.EndpointSettingKey));

        using var reloaded = CreateService(repository);
        var endpoint = await reloaded.GetActiveEndpointAsync();

        Assert.Equal("tcps://qdrant-host.lan:2376", endpoint.Display);
        Assert.True(endpoint.VerifyTls);
    }

    /// <summary>
    /// Pointing the app at a plain TCP daemon is allowed but always warns — that endpoint hands
    /// root on the target host to anyone who can reach the port.
    /// </summary>
    [Fact]
    public async Task PlainTcpEndpointRaisesWarning()
    {
        using var service = CreateService(new FakeInstanceSettingRepository());

        var endpoint = await service.ConfigureEndpointAsync(
            DockerDaemonEndpointKind.NetworkHost, "qdrant-host.lan:2375");

        Assert.True(endpoint.HasSecurityWarning);
        Assert.Equal(DockerDaemonEndpoint.PlainTcpWarningKey, endpoint.SecurityWarningKey);
    }

    /// <summary>
    /// A settings-store outage falls back to configuration and keeps the endpoint honest, rather
    /// than throwing on a page the operator opened to diagnose an outage.
    /// </summary>
    [Fact]
    public async Task SettingsStoreOutageFallsBackToConfiguration()
    {
        using var service = CreateService(
            new FakeInstanceSettingRepository { Broken = true },
            (DockerDaemonSettingsStore.EndpointConfigKey, "tcps://qdrant-host.lan:2376"));

        var endpoint = await service.GetActiveEndpointAsync();

        Assert.Equal("tcps://qdrant-host.lan:2376", endpoint.Display);
    }

    /// <summary>
    /// A daemon that is not listening is reported as exactly that, naming the endpoint that was
    /// tried. This app has a logged history of a vector-store outage surfacing as "workspace does
    /// not exist", which sent the operator to entirely the wrong problem; a transport failure must
    /// never be dressed up as an application-level condition.
    /// </summary>
    [Fact]
    public async Task UnreachableEndpointReportsTheRealReason()
    {
        using var service = CreateService(new FakeInstanceSettingRepository());
        // Port 1 on loopback: nothing listens there, so the connection is refused immediately.
        var endpoint = DockerDaemonEndpoint.Parse("tcp://127.0.0.1:1");

        var result = await service.TestConnectionAsync(endpoint);

        Assert.False(result.Success);
        Assert.Equal(endpoint.Display, result.Endpoint.Display);
        Assert.NotNull(result.Failure);
        Assert.Contains("tcp://127.0.0.1:1", result.Failure!.Arguments);
        Assert.NotNull(result.FailureKind);
        Assert.Null(result.DaemonVersion);

        // The one thing that must never happen: a connection failure described as success, or as
        // a missing container / collection / workspace. Asserted on the RENDERED sentence, in both
        // shipped languages, because the key is what a screen resolves (REQ-UI-055).
        foreach (var culture in new[] { "en", "hi" })
        {
            using var resources = new ResourceHarness(culture);
            var rendered = resources.Require(result.Failure.MessageKey, [.. result.Failure.Arguments]);

            Assert.Contains("tcp://127.0.0.1:1", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("workspace", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("collection", rendered, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Connection refused is named as connection refused, not as a generic error, so the operator
    /// knows to look at whether the daemon is running rather than at credentials.
    /// </summary>
    [Fact]
    public async Task ConnectionRefusedIsNamedAsSuch()
    {
        using var service = CreateService(new FakeInstanceSettingRepository());

        var result = await service.TestConnectionAsync(DockerDaemonEndpoint.Parse("tcp://127.0.0.1:1"));

        Assert.False(result.Success);
        Assert.Equal(DockerContainerService.ConnectionRefusedKey, result.Failure!.MessageKey);
    }

    /// <summary>
    /// A host that does not resolve is reported as a name-resolution problem against the endpoint
    /// that was tried, not as "Docker is not installed".
    /// </summary>
    [Fact]
    public async Task UnresolvableHostIsReportedAsSuch()
    {
        using var service = CreateService(new FakeInstanceSettingRepository());
        var endpoint = DockerDaemonEndpoint.Parse("tcp://no-such-daemon.invalid:2375");

        var result = await service.TestConnectionAsync(endpoint);

        Assert.False(result.Success);
        Assert.Contains("no-such-daemon.invalid", result.Failure!.Arguments.First(), StringComparison.Ordinal);

        using var resources = new ResourceHarness("hi");
        var rendered = resources.Require(result.Failure.MessageKey, [.. result.Failure.Arguments]);
        Assert.DoesNotContain("not installed", rendered, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <see cref="IDockerContainerService.IsDockerAvailableAsync"/> and
    /// <see cref="IDockerContainerService.LastFailure"/> agree: when the daemon is unavailable there
    /// is always a reason to show, never a bare "false" with a blank explanation.
    /// </summary>
    [Fact]
    public async Task UnavailableDaemonAlwaysLeavesAReason()
    {
        using var service = CreateService(
            new FakeInstanceSettingRepository(),
            (DockerDaemonSettingsStore.EndpointConfigKey, "tcp://127.0.0.1:1"));

        var available = await service.IsDockerAvailableAsync();

        Assert.False(available);
        Assert.NotNull(service.LastFailure);
        Assert.Contains("tcp://127.0.0.1:1", service.LastFailure!.Arguments);
    }

    /// <summary>
    /// A missing local Docker socket is described as a missing socket on this machine, which is
    /// the actionable truth, instead of a raw sockets error code.
    /// </summary>
    [Fact]
    public void MissingLocalSocketIsDescribedPlainly()
    {
        var endpoint = DockerDaemonEndpoint.Parse("unix:///var/run/definitely-not-a-docker.sock");

        var reason = DockerContainerService.DescribeFailure(
            endpoint, new IOException("connect failed", new FileNotFoundException("socket missing")));

        Assert.Equal(DockerContainerService.LocalSocketMissingKey, reason.MessageKey);
        Assert.Contains("definitely-not-a-docker.sock", reason.Arguments.Single(), StringComparison.Ordinal);

        using var resources = new ResourceHarness("en");
        Assert.Contains(
            "not running on this machine",
            resources.Require(reason.MessageKey, [.. reason.Arguments]),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A TLS handshake failure says the certificate is untrusted and explicitly refuses to suggest
    /// turning verification off as the fix.
    /// </summary>
    [Fact]
    public void TlsHandshakeFailureIsDescribedAsATrustProblem()
    {
        var endpoint = DockerDaemonEndpoint.Parse("tcps://qdrant-host.lan:2376");

        var reason = DockerContainerService.DescribeFailure(
            endpoint,
            new HttpRequestException("boom",
                new System.Security.Authentication.AuthenticationException("remote certificate is invalid")));

        Assert.Equal(DockerContainerService.TlsHandshakeFailedKey, reason.MessageKey);
        Assert.Contains("tcps://qdrant-host.lan:2376", reason.Arguments);

        using var resources = new ResourceHarness("en");
        var rendered = resources.Require(reason.MessageKey, [.. reason.Arguments]);
        Assert.Contains("TLS handshake", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not trusted", rendered, StringComparison.OrdinalIgnoreCase);
    }
}
