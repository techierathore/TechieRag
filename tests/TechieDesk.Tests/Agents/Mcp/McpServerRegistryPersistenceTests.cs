using Dapper;
using TechieDesk.Services.Agents.Mcp;
using TechieRag.Mcp;
using Xunit;

namespace TechieDesk.Tests.Agents.Mcp;

/// <summary>
/// REQ-RAG-023 (BRD-86) — admin-registered MCP servers really persist, really stay inside their
/// workspace, and really keep their credentials out of the database.
/// </summary>
/// <remarks>
/// Every reload goes through <see cref="McpTestHost.NewRegistry"/>, which builds a completely new
/// object graph over the same file. That is deliberate: the defect this requirement closes is that
/// the library's <c>InMemoryMcpServerRegistry</c> forgets everything on exit, and a test that reused
/// one registry instance would pass against exactly that registry.
/// </remarks>
public sealed class McpServerRegistryPersistenceTests : IDisposable
{
    private const string Finance = "ws-finance";
    private const string Marketing = "ws-marketing";

    private static readonly DateTime Now = new(2026, 7, 31, 9, 0, 0, DateTimeKind.Utc);

    private readonly McpTestHost host = new();

    /// <inheritdoc />
    public void Dispose() => host.Dispose();

    /// <summary>
    /// The shipped migration really created the table and index, with the constrained names the
    /// coding standards require. A silently missing table would otherwise surface as a runtime
    /// failure on a user's machine.
    /// </summary>
    [Fact]
    public async Task MigrationCreatesTheMcpServerSchema()
    {
        using var connection = host.OpenConnection();
        var names = (await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type IN ('table','index');")).ToList();

        Assert.Contains("WorkspaceMcpServer", names);
        Assert.Contains("IXWorkspaceMcpServerWorkspaceId", names);
    }

    /// <summary>Re-running the migrator applies nothing new — DbUp journals what it has run.</summary>
    [Fact]
    public async Task ReRunningTheMigratorAppliesNothingNew()
    {
        Assert.Equal(0, TechieDeskDb.MigrationRunner.Run("Sqlite", host.ConnectionString));

        using var connection = host.OpenConnection();
        var applied = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM \"SchemaVersions\" WHERE \"ScriptName\" LIKE '%0007-McpServer%';");

        Assert.Equal(1, applied);
    }

    /// <summary>
    /// A registered server survives a completely rebuilt service graph on the same file — the exact
    /// failure of the in-memory registry, and the reason this requirement needed an app half at all.
    /// </summary>
    [Fact]
    public async Task RegistrationSurvivesARestart()
    {
        await host.NewRegistry(Now).RegisterAsync(HttpServer("ledger", "https://mcp.example.com/rpc"));

        var reloaded = await host.NewRegistry(Now.AddDays(1)).ListAsync(Finance);

        var only = Assert.Single(reloaded);
        Assert.Equal("ledger", only.Server.Name);
        Assert.Equal(McpTransportKind.Http, only.Server.Transport);
        Assert.Equal("https://mcp.example.com/rpc", only.Server.Endpoint);
        Assert.Equal(45, only.Server.TimeoutSeconds);
        Assert.Equal(["lookup"], only.Server.AllowedTools);
        Assert.True(only.IsEnabled);
        Assert.Equal(Now, only.RegisteredAtUtc);
    }

    /// <summary>
    /// A stdio server's executable path, argument LIST and working directory survive a restart
    /// intact — the arguments as separate elements, never re-joined into a command line that would
    /// then have to be re-split.
    /// </summary>
    [Fact]
    public async Task StdioArgumentListSurvivesARestartAsSeparateArguments()
    {
        await host.NewRegistry(Now).RegisterAsync(new McpServerRegistration
        {
            WorkspaceId = Finance,
            Server = new McpServerConfig
            {
                Name = "local-index",
                Transport = McpTransportKind.Stdio,
                Command = StdioMcpServerFixture.ShellPath,
                Arguments = ["--root", "/Users/Some One/My Documents", "--verbose"],
                WorkingDirectory = "/tmp"
            }
        });

        var reloaded = Assert.Single(await host.NewRegistry().ListAsync(Finance));

        Assert.Equal(
            ["--root", "/Users/Some One/My Documents", "--verbose"],
            reloaded.Server.Arguments);
        Assert.Equal("/tmp", reloaded.Server.WorkingDirectory);
    }

    /// <summary>
    /// One workspace's registrations are invisible to another, and two workspaces may each register
    /// a server of the same name without colliding. An MCP server is a capability grant; leaking one
    /// across workspaces would hand an agent a tool its owner never approved.
    /// </summary>
    [Fact]
    public async Task RegistrationsDoNotLeakBetweenWorkspaces()
    {
        var registry = host.NewRegistry(Now);
        await registry.RegisterAsync(HttpServer("ledger", "https://finance.example.com/rpc"));
        await registry.RegisterAsync(HttpServer("ledger", "https://marketing.example.com/rpc", Marketing));

        var finance = Assert.Single(await host.NewRegistry().ListAsync(Finance));
        var marketing = Assert.Single(await host.NewRegistry().ListAsync(Marketing));

        Assert.Equal("https://finance.example.com/rpc", finance.Server.Endpoint);
        Assert.Equal("https://marketing.example.com/rpc", marketing.Server.Endpoint);
        Assert.Empty(await host.NewRegistry().ListAsync("ws-never-configured"));
    }

    /// <summary>
    /// Removing a workspace's server leaves the other workspace's identically named server alone.
    /// </summary>
    [Fact]
    public async Task RemovingAServerOnlyAffectsItsOwnWorkspace()
    {
        var registry = host.NewRegistry(Now);
        await registry.RegisterAsync(HttpServer("ledger", "https://finance.example.com/rpc"));
        await registry.RegisterAsync(HttpServer("ledger", "https://marketing.example.com/rpc", Marketing));

        Assert.True(await host.NewRegistry().UnregisterAsync(Finance, "ledger"));

        Assert.Empty(await host.NewRegistry().ListAsync(Finance));
        Assert.Single(await host.NewRegistry().ListAsync(Marketing));
    }

    /// <summary>
    /// Suspending a server keeps its configuration and survives a restart, so "disable" is not a
    /// disguised delete that costs the administrator their endpoint and token.
    /// </summary>
    [Fact]
    public async Task DisablingKeepsTheConfigurationAndSurvivesARestart()
    {
        await host.NewRegistry(Now).RegisterAsync(HttpServer("ledger", "https://mcp.example.com/rpc"));

        Assert.True(await host.NewRegistry().SetEnabledAsync(Finance, "ledger", isEnabled: false));

        var reloaded = Assert.Single(await host.NewRegistry().ListAsync(Finance));
        Assert.False(reloaded.IsEnabled);
        Assert.Equal("https://mcp.example.com/rpc", reloaded.Server.Endpoint);

        Assert.True(await host.NewRegistry().SetEnabledAsync(Finance, "ledger", isEnabled: true));
        Assert.True(Assert.Single(await host.NewRegistry().ListAsync(Finance)).IsEnabled);
    }

    /// <summary>
    /// Re-registering the same name replaces the server rather than accumulating a second row, and
    /// keeps the original registration timestamp — an edit is not a new registration.
    /// </summary>
    [Fact]
    public async Task ReRegisteringReplacesTheServerInPlace()
    {
        await host.NewRegistry(Now).RegisterAsync(HttpServer("ledger", "https://old.example.com/rpc"));
        await host.NewRegistry(Now.AddDays(3)).RegisterAsync(
            HttpServer("ledger", "https://new.example.com/rpc", registeredAtUtc: Now.AddDays(3)));

        var reloaded = Assert.Single(await host.NewRegistry().ListAsync(Finance));
        Assert.Equal("https://new.example.com/rpc", reloaded.Server.Endpoint);
        Assert.Equal(Now, reloaded.RegisteredAtUtc);

        using var connection = host.OpenConnection();
        Assert.Equal(1, await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM \"WorkspaceMcpServer\";"));
    }

    /// <summary>
    /// The credential VALUE is never written to the database — not to any column, under any name.
    /// The row keeps only the header's name and a reference, so a copied database file, a backup or
    /// a support bundle carries no token (REQ-FN-039).
    /// </summary>
    [Fact]
    public async Task TheDatabaseNeverHoldsTheCredentialValue()
    {
        const string token = "Bearer super-secret-mcp-token";

        await host.NewRegistry(Now).RegisterAsync(HttpServer(
            "ledger",
            "https://mcp.example.com/rpc",
            headers: new Dictionary<string, string> { ["Authorization"] = token }));

        var wholeFile = await File.ReadAllTextAsync(host.DatabasePath, System.Text.Encoding.Latin1);
        Assert.DoesNotContain("super-secret-mcp-token", wholeFile, StringComparison.Ordinal);

        using var connection = host.OpenConnection();
        var secretKeyNames = await connection.QuerySingleAsync<string>(
            "SELECT \"SecretKeyNames\" FROM \"WorkspaceMcpServer\";");
        var credentialRef = await connection.QuerySingleAsync<string?>(
            "SELECT \"CredentialRef\" FROM \"WorkspaceMcpServer\";");

        Assert.Contains("Authorization", secretKeyNames, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", secretKeyNames, StringComparison.Ordinal);
        Assert.Equal(McpSecretStore.CredentialRef(Finance, "ledger"), credentialRef);
    }

    /// <summary>
    /// The credential comes BACK on a modelled restart, through the encrypted sidecar, so an
    /// operator on an un-entitled build (REQ-FN-043) does not have to re-type a bearer token every
    /// launch. It is the sidecar and not a plaintext file: the stored bytes do not contain the token.
    /// </summary>
    [Fact]
    public async Task CredentialSurvivesARestartWithoutEverBeingWrittenInClear()
    {
        const string token = "Bearer super-secret-mcp-token";

        await host.NewRegistry(Now).RegisterAsync(HttpServer(
            "ledger",
            "https://mcp.example.com/rpc",
            headers: new Dictionary<string, string> { ["Authorization"] = token }));

        var reloaded = Assert.Single(await host.NewRegistry().ListAsync(Finance));
        Assert.Equal(token, reloaded.Server.Headers["Authorization"]);

        Assert.True(File.Exists(host.SecretFilePath));
        var sidecar = await File.ReadAllTextAsync(host.SecretFilePath);
        Assert.DoesNotContain("super-secret-mcp-token", sidecar, StringComparison.Ordinal);
        Assert.Contains(McpSecretStore.EncryptedPrefix, sidecar, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no durable store at all — no keychain and no Data Protection provider — the credential
    /// is lost on restart, the NAME survives on the row, and the screen can therefore say which
    /// credential to re-enter instead of the server silently calling out unauthenticated.
    /// </summary>
    [Fact]
    public async Task WithoutAnyDurableStoreTheNameSurvivesAndTheValueDoesNot()
    {
        using var noStoreHost = new McpTestHost(useEncryptedSidecar: false);

        Assert.Equal(McpCredentialProtection.MemoryOnly, noStoreHost.SecretStore.Protection);

        await noStoreHost.NewRegistry(Now).RegisterAsync(HttpServer(
            "ledger",
            "https://mcp.example.com/rpc",
            headers: new Dictionary<string, string> { ["Authorization"] = "Bearer volatile" }));

        // A process restart also empties the in-memory platform store, which is exactly what
        // EphemeralSecretStore models. The database file is untouched.
        var record = Assert.Single(
            await noStoreHost.NewRegistryWithForgottenSecrets().ListRecordsAsync(Finance));

        Assert.Equal(["Authorization"], record.SecretKeyNames);
        Assert.Empty(record.Registration.Server.Headers);
        Assert.Equal(["Authorization"], record.UnrecoverableSecretKeyNames());
    }

    /// <summary>
    /// Removing a registration removes its credential too, so a revoked token is not left
    /// recoverable from the platform store after the server it belonged to is gone.
    /// </summary>
    [Fact]
    public async Task RemovingAServerAlsoRemovesItsCredential()
    {
        await host.NewRegistry(Now).RegisterAsync(HttpServer(
            "ledger",
            "https://mcp.example.com/rpc",
            headers: new Dictionary<string, string> { ["Authorization"] = "Bearer gone-tomorrow" }));

        Assert.NotEmpty(host.SecretStore.Read(Finance, "ledger"));

        await host.NewRegistry().UnregisterAsync(Finance, "ledger");

        Assert.Empty(host.SecretStore.Read(Finance, "ledger"));
    }

    /// <summary>
    /// A configuration the trust policy forbids is refused before it reaches the table, so nothing
    /// stored can ever be launched unsafely later. Plaintext HTTP to a non-loopback host would put
    /// the server's bearer token on the wire in the clear.
    /// </summary>
    [Fact]
    public async Task PlaintextHttpToARemoteHostIsRefusedAndStoresNothing()
    {
        var failure = await Assert.ThrowsAsync<McpConfigurationException>(
            () => host.NewRegistry(Now).RegisterAsync(HttpServer("ledger", "http://mcp.example.com/rpc")));

        Assert.Contains(failure.Problems, problem =>
            problem.Contains("plaintext http", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await host.NewRegistry().ListAsync(Finance));
    }

    /// <summary>
    /// A stdio server named by a bare executable name is refused: resolving it would mean searching
    /// <c>PATH</c>, which makes what actually runs depend on the user's environment rather than on
    /// the configuration that was reviewed.
    /// </summary>
    [Fact]
    public async Task ABareExecutableNameIsRefused()
    {
        var registration = new McpServerRegistration
        {
            WorkspaceId = Finance,
            Server = new McpServerConfig
            {
                Name = "npx-server",
                Transport = McpTransportKind.Stdio,
                Command = "npx"
            }
        };

        var failure = await Assert.ThrowsAsync<McpConfigurationException>(
            () => host.NewRegistry(Now).RegisterAsync(registration));

        Assert.Contains(failure.Problems, problem =>
            problem.Contains("fully-qualified", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await host.NewRegistry().ListAsync(Finance));
    }

    /// <summary>
    /// A discovered tool list is cached on the row and survives a restart, so the Agents screen can
    /// show what a server offers without contacting it on every page load.
    /// </summary>
    [Fact]
    public async Task DiscoveredToolsAreCachedAndSurviveARestart()
    {
        await host.NewRegistry(Now).RegisterAsync(HttpServer("ledger", "https://mcp.example.com/rpc"));

        Assert.True(await host.NewRegistry().RecordDiscoveredToolsAsync(
            Finance,
            "ledger",
            [new McpToolDescriptor("lookup", "Looks an account up", "{}")],
            Now));

        var record = Assert.Single(await host.NewRegistry().ListRecordsAsync(Finance));
        var tool = Assert.Single(record.AdvertisedTools);

        Assert.Equal("lookup", tool.Name);
        Assert.Equal("Looks an account up", tool.Description);
        Assert.Equal(Now, record.LastCheckedUtc);
    }

    /// <summary>
    /// Re-registering a server clears its cached tool list, because a changed endpoint may be a
    /// different server entirely and the previous one's tools would be a fabricated list.
    /// </summary>
    [Fact]
    public async Task ReRegisteringClearsTheCachedToolList()
    {
        await host.NewRegistry(Now).RegisterAsync(HttpServer("ledger", "https://old.example.com/rpc"));
        await host.NewRegistry().RecordDiscoveredToolsAsync(
            Finance, "ledger", [new McpToolDescriptor("lookup", "Looks an account up", "{}")], Now);

        await host.NewRegistry(Now).RegisterAsync(HttpServer("ledger", "https://new.example.com/rpc"));

        var record = Assert.Single(await host.NewRegistry().ListRecordsAsync(Finance));
        Assert.Empty(record.AdvertisedTools);
        Assert.Null(record.LastCheckedUtc);
    }

    private static McpServerRegistration HttpServer(
        string name,
        string endpoint,
        string workspaceId = Finance,
        IReadOnlyDictionary<string, string>? headers = null,
        DateTime? registeredAtUtc = null) => new()
        {
            WorkspaceId = workspaceId,
            RegisteredAtUtc = registeredAtUtc ?? Now,
            Server = new McpServerConfig
            {
                Name = name,
                Transport = McpTransportKind.Http,
                Endpoint = endpoint,
                TimeoutSeconds = 45,
                AllowedTools = ["lookup"],
                Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            }
        };
}
