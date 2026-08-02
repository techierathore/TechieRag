using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Connectors;
using TechieDesk.Services.Data;
using TechieDeskDb;
using TechieRag.Connectors;
using Xunit;

namespace TechieDesk.Tests.Connectors;

/// <summary>
/// REQ-RAG-019 / REQ-RAG-020 storage: a saved connector, its credential and its sync state, against
/// a real SQLite file migrated by the real DbUp script (0006-Connectors.sql).
/// </summary>
/// <remarks>
/// <para>Nothing is hand-rolled. The migration is the shipped one, the repository is the production
/// Dapper implementation, and the credential store is the production one over the same
/// <see cref="ISecretStore"/> seam REQ-FN-039 defines. What is asserted is what the DATABASE holds —
/// a credential test that only checked a getter would pass on an implementation that also wrote the
/// token to a column.</para>
/// <para>No sleeping and no polling: every operation here completes before the next line runs.</para>
/// </remarks>
public sealed class ConnectorStoreTests : IDisposable
{
    private const string Token = "ghp_ThisIsTheSecretNobodyMayPersist";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "techiedesk-connector-store", Guid.NewGuid().ToString("N"));

    private readonly string connectionString;

    /// <summary>Creates a temporary database and migrates it.</summary>
    public ConnectorStoreTests()
    {
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "techiedesk.db"),
        }.ToString();

        Assert.Equal(0, MigrationRunner.Run("Sqlite", connectionString));
    }

    /// <summary>Removes the temporary database and any sidecar written beside it.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Re-running the migrator applies nothing, because DbUp journals what it applied.</summary>
    [Fact]
    public void ASecondMigrationRunAppliesNothing()
    {
        Assert.Equal(0, MigrationRunner.Run("Sqlite", connectionString));

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """SELECT COUNT(*) FROM "SchemaVersions" WHERE "ScriptName" LIKE '%0006-Connectors%';""";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    /// <summary>
    /// The token an operator types is in the OS credential store and in NO column of the connector
    /// row — not in <c>Settings</c>, not in <c>CredentialRef</c>, not anywhere.
    /// </summary>
    /// <remarks>
    /// This is the test that reddens if anyone "simplifies" credential handling by putting the token
    /// on the row. It reads every value of every column as text and looks for the secret, rather than
    /// naming the columns it expects to be clean — a new column holding a token would slip past the
    /// narrower assertion.
    /// </remarks>
    [Fact]
    public async Task ATokenIsStoredInTheCredentialStoreAndInNoDatabaseColumn()
    {
        using var host = CreateHost();
        var connectorId = await host.Registry.SaveAsync(RepositoryRegistration(Token));

        var cells = ReadAllCells("Connector");
        Assert.NotEmpty(cells);
        Assert.DoesNotContain(cells, cell => cell.Contains(Token, StringComparison.Ordinal));
        Assert.DoesNotContain(cells, cell => cell.Contains("ghp_", StringComparison.Ordinal));

        // And it really is somewhere: the row says a credential exists, and the OS store holds it
        // under the key derived from the connector id.
        var definition = await host.Repository.GetAsync(connectorId);
        Assert.NotNull(definition);
        Assert.True(definition.HasCredential);
        Assert.Equal($"secret:connector:{connectorId}", definition.CredentialRef);
        Assert.Equal(Token, host.Secrets.Read(ConnectorSecretStore.SecretKeyPrefix + connectorId));
        Assert.Equal(Token, host.SecretStore.Read(connectorId));
    }

    /// <summary>
    /// Removing a connector removes its stored token as well, so a revoked source leaves nothing
    /// recoverable behind.
    /// </summary>
    [Fact]
    public async Task DeletingAConnectorRemovesItsTokenAndItsSyncState()
    {
        using var host = CreateHost();
        var connectorId = await host.Registry.SaveAsync(RepositoryRegistration(Token));
        await host.Repository.SaveSyncAsync(
            connectorId,
            new ConnectorSyncState { ItemVersions = { ["readme.md"] = "sha-1" } });

        await host.Registry.DeleteAsync(connectorId);

        Assert.Null(await host.Repository.GetAsync(connectorId));
        Assert.Null(await host.Repository.GetSyncAsync(connectorId));
        Assert.Null(host.SecretStore.Read(connectorId));
    }

    /// <summary>
    /// Sync state written by one process is read back by the next, which is the whole reason it is a
    /// table.
    /// </summary>
    /// <remarks>
    /// <para>The "restart" is real: every service is disposed and rebuilt, and the connection pool is
    /// cleared, so the second half of this test reads the FILE and not a cached object graph. Both
    /// halves run on the NON-durable credential path — an in-memory <see cref="ISecretStore"/>, which
    /// is what an unsigned Mac Catalyst build actually gets — so the token has to survive the restart
    /// through the machine-bound encrypted sidecar or the second resolve fails outright.</para>
    /// </remarks>
    [Fact]
    public async Task SyncStateSurvivesARestart()
    {
        string connectorId;
        using (var first = CreateHost(new EphemeralSecretStore()))
        {
            connectorId = await first.Registry.SaveAsync(RepositoryRegistration(Token));
            await first.Resolver.SaveSyncAsync(
                (await first.Registry.CreatePayloadAsync(connectorId))!,
                new ConnectorSyncState
                {
                    LastRunUtc = new DateTimeOffset(2026, 7, 28, 6, 0, 0, TimeSpan.Zero),
                    ItemVersions =
                    {
                        ["readme.md"] = "sha-1",
                        ["docs/guide.md"] = "sha-2",
                    },
                },
                CancellationToken.None);
        }

        SqliteConnection.ClearAllPools();

        using var second = CreateHost(new EphemeralSecretStore());
        Assert.Equal(Token, second.SecretStore.Read(connectorId));

        var resolved = await second.Resolver.ResolveAsync(
            (await second.Registry.CreatePayloadAsync(connectorId))!, CancellationToken.None);

        Assert.NotNull(resolved.PreviousSync);
        Assert.Equal(2, resolved.PreviousSync.ItemVersions.Count);
        Assert.Equal("sha-1", resolved.PreviousSync.ItemVersions["readme.md"]);
        Assert.Equal("sha-2", resolved.PreviousSync.ItemVersions["docs/guide.md"]);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 28, 6, 0, 0, TimeSpan.Zero),
            resolved.PreviousSync.LastRunUtc);

        // And the connector itself came back intact, aimed at the same branch.
        Assert.Equal("repository", resolved.Connector.SourceType);
        Assert.Equal("techie/handbook@main", resolved.Connector.SourceName);
    }

    /// <summary>
    /// A connector whose row says it has a credential, but whose store no longer holds one, FAILS —
    /// it does not quietly read the source anonymously.
    /// </summary>
    /// <remarks>
    /// The anonymous fallback is the dangerous one: a private repository read without a token lists
    /// as empty, and the run reports a clean "0 ingested of 0 listed" that is indistinguishable from
    /// a source with nothing in it.
    /// </remarks>
    [Fact]
    public async Task AConnectorWhoseTokenHasVanishedFailsInsteadOfReadingAnonymously()
    {
        using var host = CreateHost();
        var connectorId = await host.Registry.SaveAsync(RepositoryRegistration(Token));
        var payload = (await host.Registry.CreatePayloadAsync(connectorId))!;

        host.Secrets.Delete(ConnectorSecretStore.SecretKeyPrefix + connectorId);

        var failure = await Assert.ThrowsAsync<ConnectorSetupException>(
            () => host.Resolver.ResolveAsync(payload, CancellationToken.None));

        Assert.Contains("could not be read", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Every shape of unusable payload is refused with a reason, before anything runs.</summary>
    [Theory]
    [InlineData("missing", "repository")]
    [InlineData("saved", "confluence")]
    public async Task AnUnusablePayloadIsRefusedWithAReason(string which, string connectorType)
    {
        using var host = CreateHost();
        var connectorId = await host.Registry.SaveAsync(RepositoryRegistration(Token));

        var payload = new ConnectorJobPayload
        {
            ConnectorId = which == "missing" ? "no-such-connector" : connectorId,
            ConnectorType = connectorType,
            DisplayName = "Handbook",
        };

        var rejected = host.Resolver.Validate(payload);
        Assert.NotNull(rejected);
        Assert.NotEmpty(rejected.ToInvariantString());
    }

    /// <summary>A payload naming a type this build cannot run is refused by name.</summary>
    [Fact]
    public void AnUnknownConnectorTypeIsRefusedByName()
    {
        using var host = CreateHost();

        var rejected = host.Resolver.Validate(new ConnectorJobPayload
        {
            ConnectorId = "anything",
            ConnectorType = "jira",
            DisplayName = "Tickets",
        });

        Assert.NotNull(rejected);
        Assert.Contains("jira", rejected.ToInvariantString(), StringComparison.Ordinal);
    }

    /// <summary>Settings that cannot produce a working connector are refused at save time.</summary>
    [Fact]
    public async Task IncompleteSettingsAreRefusedAtSaveTime()
    {
        using var host = CreateHost();

        var noProject = new ConnectorRegistration
        {
            ConnectorType = ConnectorTypes.Repository,
            DisplayName = "Broken repository",
            Settings = new ConnectorSettings { Host = "GitHub" },
        };
        Assert.NotNull(host.Registry.Validate(noProject));
        await Assert.ThrowsAsync<ConnectorSetupException>(() => host.Registry.SaveAsync(noProject));

        var bothTargets = new ConnectorRegistration
        {
            ConnectorType = ConnectorTypes.Confluence,
            DisplayName = "Broken wiki",
            Settings = new ConnectorSettings
            {
                BaseUrl = "https://acme.atlassian.net/wiki",
                SpaceKey = "ENG",
                RootPageId = "12345",
            },
        };
        Assert.NotNull(host.Registry.Validate(bothTargets));

        // And a private-network target without the opt-in is named as such, rather than failing
        // later as an unexplained connection refusal.
        var privateSite = new ConnectorRegistration
        {
            ConnectorType = ConnectorTypes.Confluence,
            DisplayName = "Internal wiki",
            Settings = new ConnectorSettings
            {
                BaseUrl = "http://192.168.1.40/confluence",
                SpaceKey = "ENG",
            },
        };
        var refusal = host.Registry.Validate(privateSite);
        Assert.NotNull(refusal);
        Assert.Contains("private network", refusal.ToInvariantString(), StringComparison.Ordinal);

        // The same settings WITH the opt-in are accepted, because the operator said so.
        Assert.Null(host.Registry.Validate(privateSite with
        {
            Settings = privateSite.Settings with { AllowPrivateNetwork = true },
        }));
    }

    /// <summary>
    /// When the OS credential store refuses the process, the token is encrypted at rest with a
    /// machine-bound key — never written in cleartext, and never written to the database.
    /// </summary>
    /// <remarks>
    /// This is the documented Mac Catalyst degradation: an unsigned build gets
    /// <c>errSecMissingEntitlement</c> from Keychain, so <c>OsCredentialStore</c> reports itself
    /// non-durable. The assertion is about the FILE's bytes, so an implementation that quietly wrote
    /// the token in the clear fails here rather than in production.
    /// </remarks>
    [Fact]
    public async Task WhenTheOsStoreIsRefusedTheTokenIsEncryptedAtRestAndStillNotInTheDatabase()
    {
        using var host = CreateHost(new EphemeralSecretStore());
        var connectorId = await host.Registry.SaveAsync(RepositoryRegistration(Token));

        var sidecar = Path.Combine(directory, ConnectorSecretStore.SecretFileName);
        Assert.True(File.Exists(sidecar), "the encrypted fallback should have written a sidecar");

        var contents = File.ReadAllText(sidecar);
        Assert.DoesNotContain(Token, contents, StringComparison.Ordinal);
        Assert.Contains(ConnectorSecretStore.EncryptedPrefix, contents, StringComparison.Ordinal);

        Assert.DoesNotContain(
            ReadAllCells("Connector"), cell => cell.Contains(Token, StringComparison.Ordinal));

        // It is still usable: the run reads it back through the same seam it was written through.
        Assert.Equal(Token, host.SecretStore.Read(connectorId));
        Assert.True(host.Registry.CredentialsAreDurable);
        Assert.Equal(
            ConnectorSecretStore.EncryptedAtRestDescriptionKey,
            host.Registry.CredentialStorageDescriptionKey);
    }

    /// <summary>
    /// Re-saving a connector without re-typing its token keeps the stored one; submitting an empty
    /// token clears it.
    /// </summary>
    [Fact]
    public async Task ReSavingWithoutATokenKeepsItAndAnEmptyTokenClearsIt()
    {
        using var host = CreateHost();
        var connectorId = await host.Registry.SaveAsync(RepositoryRegistration(Token));

        await host.Registry.SaveAsync(RepositoryRegistration(accessToken: null) with
        {
            ConnectorId = connectorId,
            Settings = new ConnectorSettings
            {
                Host = "GitHub",
                ProjectPath = "techie/handbook",
                Branch = "release",
            },
        });

        var kept = await host.Repository.GetAsync(connectorId);
        Assert.NotNull(kept);
        Assert.True(kept.HasCredential);
        Assert.Equal("release", kept.ReadSettings().Branch);
        Assert.Equal(Token, host.SecretStore.Read(connectorId));

        await host.Registry.SaveAsync(RepositoryRegistration(accessToken: string.Empty) with
        {
            ConnectorId = connectorId,
        });

        var cleared = await host.Repository.GetAsync(connectorId);
        Assert.NotNull(cleared);
        Assert.False(cleared.HasCredential);
        Assert.Null(host.SecretStore.Read(connectorId));
    }

    /// <summary>Builds the registration used throughout these tests.</summary>
    private ConnectorRegistration RepositoryRegistration(string? accessToken) => new()
    {
        ConnectorType = ConnectorTypes.Repository,
        DisplayName = "Handbook",
        Settings = new ConnectorSettings
        {
            Host = "GitHub",
            ProjectPath = "techie/handbook",
            Branch = "main",
            IncludeGlobs = ["**/*.md"],
        },
        AccessToken = accessToken,
    };

    /// <summary>Reads every cell of a table as text, for "the secret is nowhere in here" assertions.</summary>
    /// <param name="table">The table to read.</param>
    /// <returns>Every non-null value of every column of every row.</returns>
    private List<string> ReadAllCells(string table)
    {
        var cells = new List<string>();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT * FROM "{table}";""";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            for (var index = 0; index < reader.FieldCount; index++)
            {
                if (!reader.IsDBNull(index))
                {
                    cells.Add(reader.GetValue(index).ToString() ?? string.Empty);
                }
            }
        }

        return cells;
    }

    /// <summary>Builds a fresh service graph over the same database file.</summary>
    /// <param name="secrets">The credential-store seam to use, or null for a durable double.</param>
    /// <returns>The graph.</returns>
    private StoreHost CreateHost(ISecretStore? secrets = null) =>
        StoreHost.Create(directory, connectionString, secrets);

    /// <summary>The production service graph, built over one database file.</summary>
    private sealed class StoreHost : IDisposable
    {
        private StoreHost(
            ISecretStore secrets,
            IConnectorSecretStore secretStore,
            IConnectorRepository repository,
            DatabaseConnectorResolver resolver,
            IConnectorRegistry registry)
        {
            Secrets = secrets;
            SecretStore = secretStore;
            Repository = repository;
            Resolver = resolver;
            Registry = registry;
        }

        public ISecretStore Secrets { get; }

        public IConnectorSecretStore SecretStore { get; }

        public IConnectorRepository Repository { get; }

        public DatabaseConnectorResolver Resolver { get; }

        public IConnectorRegistry Registry { get; }

        public static StoreHost Create(
            string directory, string connectionString, ISecretStore? secrets)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [DataDirectory.ConfigKey] = directory,
                })
                .Build();

            var store = secrets ?? new DurableSecretStoreDouble();
            var secretStore = new ConnectorSecretStore(
                store,
                configuration,
                NullLogger<ConnectorSecretStore>.Instance,
                DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys"))));

            var repository = new ConnectorRepository(new AppDbConnectionFactory(
                Options.Create(new AppDbOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = connectionString,
                })));

            var resolver = new DatabaseConnectorResolver(
                repository,
                secretStore,
                NullLoggerFactory.Instance,
                NullLogger<DatabaseConnectorResolver>.Instance,
                TimeProvider.System);

            var registry = new ConnectorRegistry(
                repository, secretStore, resolver, NullLogger<ConnectorRegistry>.Instance);

            return new StoreHost(store, secretStore, repository, resolver, registry);
        }

        public void Dispose() => Resolver.Dispose();
    }
}
