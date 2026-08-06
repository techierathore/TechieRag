using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Agents.Mcp;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Data;
using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.Agents.Mcp;

/// <summary>
/// A disposable TechieDesk data directory and app database for the MCP tests, built by running the
/// SHIPPED DbUp migrations (REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>The migration is exercised, not bypassed.</b> Hand-writing <c>CREATE TABLE</c> here would
/// let <c>0007-McpServer.sql</c> fail to apply, or drift from the Dapper parameter names, and every
/// test would still pass — the failure would surface at a user's first launch instead. So the
/// harness runs the real migrator against a real file.</para>
/// <para><b><see cref="NewRegistry"/> models a restart.</b> Every call builds a completely new object
/// graph over the same file: new connection factory, new registry, nothing carried in memory. That
/// is the only way to tell durable storage apart from the process-lifetime registry this
/// requirement exists to replace — a test that reused one instance would pass against
/// <c>InMemoryMcpServerRegistry</c>.</para>
/// <para><b>The secret store is deliberately the un-entitled configuration.</b>
/// <see cref="EphemeralSecretStore"/> reports itself non-durable, exactly as an unsigned Mac
/// Catalyst build does while REQ-FN-043 is blocked, so the Data-Protection sidecar is the tier under
/// test. That is the tier real users are on today.</para>
/// </remarks>
public sealed class McpTestHost : IDisposable
{
    private readonly string dataDirectory;
    private readonly EphemeralSecretStore platformSecrets = new();
    private readonly IDataProtectionProvider? dataProtection;

    /// <summary>Creates the data directory, the database, and applies every shipped migration.</summary>
    /// <param name="useEncryptedSidecar">
    /// True to supply a Data Protection provider, so credentials land in the REQ-NFR-004b sidecar;
    /// false to model a host with no durable store at all.
    /// </param>
    public McpTestHost(bool useEncryptedSidecar = true)
    {
        dataDirectory = Path.Combine(Path.GetTempPath(), $"techiedesk-mcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);

        DatabasePath = Path.Combine(dataDirectory, "techiedesk.db");
        Assert.Equal(0, MigrationRunner.Run("Sqlite", ConnectionString));

        dataProtection = useEncryptedSidecar
            ? DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dataDirectory, "keys")))
            : null;

        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataDirectory.ConfigKey] = dataDirectory,
                ["AppDb:Provider"] = "Sqlite",
                ["AppDb:ConnectionString"] = ConnectionString
            })
            .Build();

        SecretStore = new McpSecretStore(
            platformSecrets, Configuration, NullLogger<McpSecretStore>.Instance, dataProtection);
    }

    /// <summary>Gets the app database file path.</summary>
    public string DatabasePath { get; }

    /// <summary>Gets the SQLite connection string for the app database.</summary>
    public string ConnectionString => $"Data Source={DatabasePath}";

    /// <summary>Gets the configuration the services are built from.</summary>
    public IConfiguration Configuration { get; }

    /// <summary>Gets the credential store every registry in this host shares.</summary>
    /// <remarks>
    /// Shared on purpose: the OS credential store IS shared between runs of the real app, so a
    /// modelled restart that also forgot its keychain would be modelling two failures at once.
    /// </remarks>
    public IMcpSecretStore SecretStore { get; }

    /// <summary>Gets the path of the encrypted credential sidecar, whether or not it exists.</summary>
    public string SecretFilePath => Path.Combine(dataDirectory, McpSecretStore.SecretFileName);

    /// <summary>Builds a connection factory over the same database file.</summary>
    /// <returns>A new factory.</returns>
    public IAppDbConnectionFactory NewConnectionFactory() =>
        new AppDbConnectionFactory(Options.Create(new AppDbOptions
        {
            Provider = "Sqlite",
            ConnectionString = ConnectionString
        }));

    /// <summary>
    /// Builds a completely fresh registry over the same file, modelling a restarted process.
    /// </summary>
    /// <param name="now">The instant the registry's clock reports.</param>
    /// <returns>A new registry.</returns>
    public SqliteMcpServerRegistry NewRegistry(DateTime? now = null) =>
        new(NewConnectionFactory(),
            SecretStore,
            new FixedTimeProvider(now ?? new DateTime(2026, 7, 31, 9, 0, 0, DateTimeKind.Utc)),
            NullLogger<SqliteMcpServerRegistry>.Instance);

    /// <summary>
    /// Builds a completely fresh workspace MCP service over the same file, modelling a restart.
    /// </summary>
    /// <param name="now">The instant the service's clock reports.</param>
    /// <returns>A new service.</returns>
    public WorkspaceMcpService NewService(DateTime? now = null)
    {
        var registry = NewRegistry(now);
        return new WorkspaceMcpService(
            registry,
            registry,
            SecretStore,
            new FixedTimeProvider(now ?? new DateTime(2026, 7, 31, 9, 0, 0, DateTimeKind.Utc)),
            NullLoggerFactory.Instance,
            NullLogger<WorkspaceMcpService>.Instance);
    }

    /// <summary>
    /// Builds a registry whose credential store has FORGOTTEN everything — the un-entitled build
    /// after a process restart, where the keychain refused and there was no sidecar to fall back to.
    /// </summary>
    /// <returns>A registry over the same database whose secrets are gone.</returns>
    /// <remarks>
    /// The database file is untouched, so this isolates the one failure being modelled: the row
    /// survives, the credential does not. Copying database files around would model two things at
    /// once and would be at the mercy of SQLite's journal files.
    /// </remarks>
    public SqliteMcpServerRegistry NewRegistryWithForgottenSecrets() =>
        new(NewConnectionFactory(),
            new McpSecretStore(
                new EphemeralSecretStore(),
                Configuration,
                NullLogger<McpSecretStore>.Instance,
                dataProtectionProvider: null),
            new FixedTimeProvider(new DateTime(2026, 7, 31, 9, 0, 0, DateTimeKind.Utc)),
            NullLogger<SqliteMcpServerRegistry>.Instance);

    /// <summary>Opens a raw connection, for assertions about what is actually on the row.</summary>
    /// <returns>An open-on-demand SQLite connection.</returns>
    public SqliteConnection OpenConnection() => new(ConnectionString);

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
    }

    /// <summary>A fixed clock, so stored timestamps are deterministic.</summary>
    /// <param name="now">The instant the clock reports.</param>
    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
