using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Connectors;
using TechieDesk.Services.Data;
using TechieDesk.Services.Scheduling;
using TechieDesk.Tests.Workspaces;
using TechieDeskDb;
using TechieRag;
using TechieRag.Connectors;
using TechieRag.Web;
using Xunit;

namespace TechieDesk.Tests.Connectors;

/// <summary>
/// REQ-RAG-019 (BRD-63) end to end: a repository connector saved on the connector hub is resolved
/// from the database, authenticated from the credential store, walked over a real socket, ingested
/// into a real catalogue — and re-syncing a CHANGED file replaces its document rather than adding a
/// second copy of it.
/// </summary>
/// <remarks>
/// <para>The only thing standing in for production here is the GitHub host itself, and it is a real
/// HTTP server on loopback rather than a substituted transport — see <see cref="FakeGitHubHost"/> for
/// why that distinction matters. Everything else is the shipped code: the DbUp migration, the Dapper
/// repository, <see cref="ConnectorSecretStore"/>, <see cref="DatabaseConnectorResolver"/>, the
/// production <see cref="BackgroundJobService"/> and <see cref="JobRunner"/> on a real thread-pool
/// thread, <see cref="ConnectorJobHandler"/>, and <see cref="RagConnectorDocumentSink"/> over a real
/// <see cref="ITechieRag"/>.</para>
/// <para>Nothing sleeps. Waiting is on <see cref="SignallingRunRepositoryDecorator"/>, which
/// completes when the run row is actually written.</para>
/// </remarks>
public sealed class ConnectorEndToEndTests : IAsyncDisposable
{
    private const string Token = "ghp_EndToEndTokenThatMustNeverBePersisted";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "techiedesk-connector-e2e", Guid.NewGuid().ToString("N"));

    /// <summary>Removes the temporary database, key ring and vector store.</summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A vector store file still held open by a finalizer must not fail the run.
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// A saved repository connector reads its source with the token from the credential store, and
    /// the files it reports as ingested really are searchable in the catalogue.
    /// </summary>
    [Fact]
    public async Task ASavedRepositoryConnectorIngestsItsFilesAndAuthenticatesFromTheCredentialStore()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Host
            .SetFile("README.md", "The handbook explains how the platform is operated.", "sha-readme-1")
            .SetFile("docs/onboarding.md", "New joiners read this on their first morning.", "sha-onboarding-1")
            .SetFile("docs/runbook.md", "What to do when the overnight sync fails.", "sha-runbook-1");

        var connectorId = await harness.SaveConnectorAsync(Token);
        var payload = (await harness.Registry.CreatePayloadAsync(connectorId))!;

        await harness.Jobs.StartAsync(payload with { RequestDelayMs = 0 });
        var run = await harness.Runs.Completed;

        Assert.Equal(RunOutcome.Succeeded, run.Outcome);
        Assert.Equal(3, run.ItemsProcessed);
        Assert.Equal(0, run.ItemsFailed);

        var documents = await harness.Rag.ListDocumentsAsync();
        Assert.Equal(3, documents.Count);
        Assert.Contains(documents, document => document.Name == "docs/runbook.md");

        // Every ingested document reports where it came from, AFTER a real store round trip. The
        // vector store lifts only "SourcePath" onto the catalogue row, so asserting the metadata
        // dictionary the sink built would have passed while the source column rendered blank —
        // exactly the defect web ingestion already hit.
        Assert.All(documents, document => Assert.StartsWith(
            harness.Host.ApiBaseUrl, document.WebSourceUrl(), StringComparison.Ordinal));

        // The ingested text is real and retrievable, not an empty shell of a document.
        var hits = await harness.Rag.SearchAsync("overnight sync fails", 50);
        Assert.Contains(
            hits, hit => hit.Chunk.Text.Contains("overnight sync fails", StringComparison.Ordinal));

        // The token travelled from the credential store onto the wire — and nowhere else.
        Assert.All(
            harness.Host.AuthorizationHeaders,
            header => Assert.Equal($"Bearer {Token}", header));
    }

    /// <summary>
    /// Re-syncing a source whose files have CHANGED replaces their documents; the catalogue does not
    /// grow, and the old text is gone.
    /// </summary>
    /// <remarks>
    /// <para>This is the regression test for the defect Cluster B found: with no item→document map,
    /// the second run of a repository whose three files had changed took the catalogue from three
    /// documents to six, and every search returned the superseded text alongside the current text.
    /// </para>
    /// <para>It reddens if <see cref="IConnectorDocumentMap"/> is removed from
    /// <see cref="RagConnectorDocumentSink"/>: the count assertion goes from 3 to 6, and the old
    /// sentence comes back into the search results.</para>
    /// </remarks>
    [Fact]
    public async Task ReSyncingAChangedFileReplacesItsDocumentInsteadOfDuplicatingIt()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Host
            .SetFile("README.md", "The handbook is currently at version one.", "sha-readme-1")
            .SetFile("docs/onboarding.md", "Onboarding, first edition.", "sha-onboarding-1")
            .SetFile("docs/runbook.md", "Runbook, first edition.", "sha-runbook-1");

        var connectorId = await harness.SaveConnectorAsync(Token);
        var payload = (await harness.Registry.CreatePayloadAsync(connectorId))! with { RequestDelayMs = 0 };

        await harness.Jobs.StartAsync(payload);
        var first = await harness.Runs.Completed;
        Assert.Equal(3, first.ItemsProcessed);
        Assert.Equal(3, (await harness.Rag.ListDocumentsAsync()).Count);

        // A version bump on every file: new content, new blob hash, same paths.
        harness.Host
            .SetFile("README.md", "The handbook is now at version two.", "sha-readme-2")
            .SetFile("docs/onboarding.md", "Onboarding, second edition.", "sha-onboarding-2")
            .SetFile("docs/runbook.md", "Runbook, second edition.", "sha-runbook-2");

        harness.ResetRunSignal();
        await harness.Jobs.StartAsync(payload);
        var second = await harness.Runs.Completed;

        Assert.Equal(RunOutcome.Succeeded, second.Outcome);
        Assert.Equal(3, second.ItemsProcessed);

        var documents = await harness.Rag.ListDocumentsAsync();
        Assert.Equal(3, documents.Count);
        Assert.Equal(3, documents.Select(document => document.Name).Distinct(StringComparer.Ordinal).Count());

        // The superseded text is GONE from the index, not merely outranked. topK is far larger
        // than the number of chunks in play, so this reads the whole catalogue rather than a ranking.
        var everything = await harness.Rag.SearchAsync("edition", 50);
        Assert.DoesNotContain(
            everything, hit => hit.Chunk.Text.Contains("first edition", StringComparison.Ordinal));
        Assert.Contains(
            everything, hit => hit.Chunk.Text.Contains("second edition", StringComparison.Ordinal));
    }

    /// <summary>
    /// Re-syncing a source whose files have NOT changed fetches nothing and ingests nothing, because
    /// the sync state written by the first run was read back from the database.
    /// </summary>
    [Fact]
    public async Task AnUnchangedSourceIsSkippedOnTheSecondRunFromPersistedSyncState()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Host
            .SetFile("README.md", "Nothing about this file is going to change.", "sha-readme-1")
            .SetFile("docs/runbook.md", "Nor this one.", "sha-runbook-1");

        var connectorId = await harness.SaveConnectorAsync(Token);
        var payload = (await harness.Registry.CreatePayloadAsync(connectorId))! with { RequestDelayMs = 0 };

        await harness.Jobs.StartAsync(payload);
        await harness.Runs.Completed;

        var stored = await harness.Repository.GetSyncAsync(connectorId);
        Assert.NotNull(stored);
        Assert.Equal(2, stored.ItemVersions.Count);
        Assert.Equal("sha-readme-1", stored.ItemVersions["README.md"]);

        harness.ResetRunSignal();
        await harness.Jobs.StartAsync(payload);
        var second = await harness.Runs.Completed;

        Assert.Equal(0, second.ItemsProcessed);
        Assert.Equal(2, second.ItemsSkipped);
        Assert.Equal(2, (await harness.Rag.ListDocumentsAsync()).Count);
    }

    /// <summary>The saved connector reads a source that needs no token at all.</summary>
    [Fact]
    public async Task AnAnonymousConnectorSendsNoAuthorizationHeader()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Host.SetFile("README.md", "A public handbook anyone may read.", "sha-readme-1");

        var connectorId = await harness.SaveConnectorAsync(accessToken: null);
        var payload = (await harness.Registry.CreatePayloadAsync(connectorId))! with { RequestDelayMs = 0 };

        await harness.Jobs.StartAsync(payload);
        var run = await harness.Runs.Completed;

        Assert.Equal(1, run.ItemsProcessed);
        Assert.All(harness.Host.AuthorizationHeaders, header => Assert.Null(header));
    }

    /// <summary>
    /// "Test connection" reaches the source and lists it, without ingesting anything.
    /// </summary>
    [Fact]
    public async Task TestingAConnectorListsItsSourceWithoutIngesting()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Host.SetFile("README.md", "Reachable.", "sha-readme-1");

        var connectorId = await harness.SaveConnectorAsync(Token);

        Assert.Null(await harness.Registry.TestAsync(connectorId));
        Assert.Empty(await harness.Rag.ListDocumentsAsync());
        Assert.Equal(1, harness.Host.ListCount);
    }

    /// <summary>
    /// A connector pointed at a source that is not there fails with a reason naming the project,
    /// never with a stack trace and never with the token.
    /// </summary>
    [Fact]
    public async Task AMissingRepositoryFailsWithAReadableReason()
    {
        await using var harness = await Harness.CreateAsync(directory);

        var connectorId = await harness.SaveConnectorAsync(Token, projectPath: "techie/not-here");
        // REQ-UI-056: TestAsync now returns codes; ToInvariantString is the English rendering, which
        // is what a credential-leak assertion has to look at.
        var failure = (await harness.Registry.TestAsync(connectorId))?.ToInvariantString();

        Assert.NotNull(failure);
        Assert.Contains("techie/not-here", failure, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-UI-057: loopback hosts built at the same moment each take a port of their own, serve on
    /// it, and hand it straight back when disposed.
    /// </summary>
    /// <remarks>
    /// <para>The flake this replaces was a fixture that bound a port some other socket had claimed
    /// while it was not looking — <c>HttpListenerException: Address already in use</c> — landing on a
    /// different test each time and passing on re-run. That is the failure mode that teaches a team
    /// to re-run instead of read, so the fixture is now asserted directly rather than trusted.</para>
    /// <para>Re-binding every port AFTER disposal is the half that cannot be argued about: a leaked
    /// listener holds its port permanently, so a leak reddens this and a rival test merely
    /// momentarily borrowing a freed port does not.</para>
    /// </remarks>
    [Fact]
    public async Task ConcurrentLoopbackHostsTakeDistinctPortsAndReleaseThemOnDisposal()
    {
        const int HostCount = 24;

        var hosts = await Task.WhenAll(Enumerable.Range(0, HostCount)
            .Select(_ => Task.Run(() => new FakeGitHubHost())));
        var ports = hosts.Select(host => new Uri(host.ApiBaseUrl).Port).ToList();

        try
        {
            Assert.Equal(HostCount, ports.Distinct().Count());

            // Bound is not the same as serving; every one of them answers.
            using var client = new HttpClient();
            foreach (var host in hosts)
            {
                using var response = await client.GetAsync($"{host.ApiBaseUrl}/repos/{host.ProjectPath}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            Array.ForEach(hosts, host => host.Dispose());
        }

        Assert.All(ports, port => Assert.True(
            IsFreeAgain(port), $"port {port} was still held after the host that owned it was disposed"));
    }

    /// <summary>Reports whether a loopback port can be bound again.</summary>
    /// <param name="port">The port a disposed host used to own.</param>
    /// <returns>True once the port binds.</returns>
    /// <remarks>
    /// A leaked listener holds its port for the life of the process, so retrying separates a real
    /// leak from another test in this assembly having been handed the freed port for a moment.
    /// </remarks>
    private static bool IsFreeAgain(int port)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                var reclaimed = new TcpListener(IPAddress.Loopback, port);
                reclaimed.Start();
                reclaimed.Stop();
                return true;
            }
            catch (SocketException)
            {
                Thread.Sleep(20 * attempt);
            }
        }

        return false;
    }

    /// <summary>The production service graph, over one database file and one loopback host.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ITechieRag rag;
        private readonly ScheduleRunRepository runRepository;
        private readonly ServiceProvider provider;
        private readonly IServiceScope registryScope;

        private Harness(
            ITechieRag rag,
            ScheduleRunRepository runRepository,
            ServiceProvider provider,
            IServiceScope registryScope,
            FakeGitHubHost host,
            IConnectorRepository repository,
            IConnectorJobService jobs,
            SignallingRunRepositoryDecorator runs)
        {
            this.rag = rag;
            this.runRepository = runRepository;
            this.provider = provider;
            this.registryScope = registryScope;
            Host = host;
            Repository = repository;
            Registry = registryScope.ServiceProvider.GetRequiredService<IConnectorRegistry>();
            Jobs = jobs;
            Runs = runs;
        }

        public FakeGitHubHost Host { get; }

        public IConnectorRepository Repository { get; }

        public IConnectorRegistry Registry { get; }

        public IConnectorJobService Jobs { get; private set; }

        public SignallingRunRepositoryDecorator Runs { get; private set; }

        public ITechieRag Rag => rag;

        public static async Task<Harness> CreateAsync(string directory)
        {
            var root = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(root, "techiedesk.db"),
            }.ToString();
            Assert.Equal(0, MigrationRunner.Run("Sqlite", connectionString));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [DataDirectory.ConfigKey] = root,
                    ["AppDb:Provider"] = "Sqlite",
                    ["AppDb:ConnectionString"] = connectionString,
                })
                .Build();

            var rag = new TechieRagBuilder()
                .UseCustomEmbeddingProvider(() => new StubEmbeddingProvider())
                .UseVectorStore(VectorStoreType.SqliteVec, $"Data Source={Path.Combine(root, "vectors.db")}")
                .WithPersistence(StoreProvider.Sqlite, $"Data Source={Path.Combine(root, "rag.db")}")
                .Build();
            await rag.InitializeAsync();

            var runRepository = new ScheduleRunRepository(new AppDbConnectionFactory(
                Options.Create(new AppDbOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = connectionString,
                })));
            var runs = new SignallingRunRepositoryDecorator(runRepository);

            // The real registrations, in the real order: data, scheduling, connectors, connector
            // jobs. Registering AddTechieDeskConnectors AFTER AddTechieDeskConnectorJobs is the
            // mistake this ordering exists to prevent — the job cluster's TryAdd would win and the
            // build would keep the "no connector types are installed" default.
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddSingleton<ISecretStore>(new DurableSecretStoreDouble());
            services.AddSingleton<IDataProtectionProvider>(
                DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keys"))));
            services.AddSingleton<ITechieRag>(rag);
            services.AddTechieDeskData(configuration);
            services.AddTechieDeskScheduling(configuration);
            services.AddSingleton<IScheduleRunRepository>(runs);
            services.AddTechieDeskConnectors();
            services.AddTechieDeskConnectorJobs();

            var provider = services.BuildServiceProvider();

            return new Harness(
                rag,
                runRepository,
                provider,
                provider.CreateScope(),
                new FakeGitHubHost(),
                provider.GetRequiredService<IConnectorRepository>(),
                provider.GetRequiredService<IConnectorJobService>(),
                runs);
        }

        /// <summary>Saves the repository connector these tests run, aimed at the loopback host.</summary>
        /// <param name="accessToken">The token to store, or null for anonymous access.</param>
        /// <param name="projectPath">The project to read, defaulting to the one the host serves.</param>
        /// <returns>The connector key.</returns>
        public Task<string> SaveConnectorAsync(string? accessToken, string? projectPath = null) =>
            Registry.SaveAsync(new ConnectorRegistration
            {
                ConnectorType = ConnectorTypes.Repository,
                DisplayName = "Handbook",
                Settings = new ConnectorSettings
                {
                    Host = "GitHub",
                    ProjectPath = projectPath ?? Host.ProjectPath,
                    Branch = "main",
                    ApiBaseUrl = Host.ApiBaseUrl,
                    WebBaseUrl = Host.ApiBaseUrl,

                    // The loopback host IS a private-network address, so this is the opt-in under
                    // test: without it the library refuses the connection at both call sites, which
                    // is exactly what a self-hosted GitLab or Confluence would hit.
                    AllowPrivateNetwork = true,
                    IncludeGlobs = ["**/*.md"],
                },
                AccessToken = accessToken,
            });

        /// <summary>Re-arms the run signal so a second run can be awaited deterministically.</summary>
        public void ResetRunSignal()
        {
            Runs = new SignallingRunRepositoryDecorator(runRepository);
            var jobRunner = new JobRunner(
                Runs,
                provider.GetServices<IScheduledJobHandler>().ToList(),
                TimeProvider.System,
                NullLogger<JobRunner>.Instance);
            var backgroundJobs = new BackgroundJobService(
                jobRunner, NullLogger<BackgroundJobService>.Instance);
            Jobs = new ConnectorJobService(
                backgroundJobs, Runs, provider.GetRequiredService<IServiceScopeFactory>());
        }

        public async ValueTask DisposeAsync()
        {
            Host.Dispose();
            registryScope.Dispose();
            await provider.DisposeAsync();
            (rag as IDisposable)?.Dispose();
        }
    }
}
