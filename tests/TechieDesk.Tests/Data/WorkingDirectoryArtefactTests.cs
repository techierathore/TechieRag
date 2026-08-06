using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using TechieDesk.Services;
using TechieDesk.Services.Hosting;
using TechieDeskDb;
using TechieRag;
using Xunit;

namespace TechieDesk.Tests.Data;

/// <summary>
/// REQ-FN-048 (BRD-130): no persistent artefact may resolve against the process working directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>These tests run production code and then look at the disk.</b> That is the whole point.
/// <c>DataDirectoryTests.EveryArtefactSharesOneDirectory</c> builds its own paths with
/// <see cref="Path.Combine"/> and asserts that they match each other — it observes no call site, and
/// it stayed green through BOTH halves of this defect: the SqliteVec store carrying the relative
/// literal <c>Data Source=techierag.db</c>, and the migration console handing Serilog the relative
/// <c>logs/techiedeskdb-.log</c>.
/// </para>
/// <para>
/// The consequence was not cosmetic. Exec'd with its working directory inside the bundle, the
/// desktop head wrote a live 24 KB <c>techierag.db</c> into <c>TechieDesk.app/</c>, after which
/// <c>codesign --verify --deep --strict</c> failed with "unsealed contents present in the bundle
/// root" and the Release build could not be signed until the file was deleted by hand.
/// </para>
/// <para>
/// Every test here changes <see cref="Directory.SetCurrentDirectory"/>, which is process-global, so
/// the class sits in a collection that does not run in parallel with anything else.
/// </para>
/// </remarks>
[Collection(WorkingDirectoryCollection.Name)]
public sealed class WorkingDirectoryArtefactTests : IDisposable
{
    /// <summary>How long the RAG store is allowed to come up before the test gives up on it.</summary>
    private static readonly TimeSpan InitializationBudget = TimeSpan.FromSeconds(60);

    private readonly string sandbox;
    private readonly string workingDirectory;
    private readonly string dataDirectory;
    private readonly string originalWorkingDirectory;

    /// <summary>Creates an empty working directory and an empty data directory, and enters the former.</summary>
    public WorkingDirectoryArtefactTests()
    {
        sandbox = Path.Combine(Path.GetTempPath(), $"techiedesk-cwd-{Guid.NewGuid():N}");
        workingDirectory = Path.Combine(sandbox, "cwd");
        dataDirectory = Path.Combine(sandbox, "data");
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(dataDirectory);

        originalWorkingDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workingDirectory);
    }

    /// <summary>Restores the working directory and removes the sandbox.</summary>
    public void Dispose()
    {
        Log.CloseAndFlush();
        Directory.SetCurrentDirectory(originalWorkingDirectory);

        try
        {
            Directory.Delete(sandbox, recursive: true);
        }
        catch (IOException)
        {
            // A log file the sink has not released yet is not worth failing a passing test over.
        }
    }

    /// <summary>
    /// Acceptance (2): a configuration written by an earlier build carries the relative default
    /// <c>Data Source=techierag.db</c>, and loading it rewrites the file to the absolute path inside
    /// the data directory while MOVING the database the relative path already created — the
    /// install's embeddings are carried across, not orphaned.
    /// </summary>
    /// <remarks>
    /// The migration is asserted through a SECOND service with an empty cache, because REQ-NFR-012
    /// encrypts the connection string at rest — the corrected value is in the file, but not as
    /// readable text. A second load is also the honest question: does the NEXT launch see the fix.
    /// </remarks>
    [Fact]
    public async Task RelativeVectorStorePathIsMigratedOnFirstLoad()
    {
        var stray = Path.Combine(workingDirectory, DataDirectory.VectorDbFileName);
        File.WriteAllText(stray, "existing embeddings");
        WriteSavedConfig("Data Source=techierag.db");

        var loaded = await CreateConfigService().LoadConfigAsync();
        var afterRestart = await CreateConfigService().LoadConfigAsync();

        var expected = DataDirectory.VectorDbConnectionString(dataDirectory);
        Assert.Equal(expected, loaded.VectorStore.ConnectionString);
        Assert.Equal(expected, afterRestart.VectorStore.ConnectionString);
        Assert.False(File.Exists(stray), "The stray working-directory database must not be orphaned.");
        Assert.Equal(
            "existing embeddings",
            File.ReadAllText(Path.Combine(dataDirectory, DataDirectory.VectorDbFileName)));
    }

    /// <summary>
    /// The Setup wizard writes the app-relative <c>data/…</c> shape rather than a bare file name, and
    /// that is corrected too: the pre-REQ-FN-037 <c>data/</c> folder was relocated INTO the data
    /// directory, so the segment is dropped instead of producing a <c>data/data/</c> nesting.
    /// </summary>
    [Fact]
    public async Task AppRelativeVectorStorePathIsMigratedOnFirstLoad()
    {
        WriteSavedConfig("Data Source=data/techiedesk-rag-store.db");

        var loaded = await CreateConfigService().LoadConfigAsync();

        Assert.Equal(
            $"Data Source={Path.Combine(dataDirectory, DataDirectory.RagStoreFileName)}",
            loaded.VectorStore.ConnectionString);
    }

    /// <summary>
    /// A connection string that is already absolute is left exactly as the operator wrote it — the
    /// migration corrects a relative path, it does not herd every install into one location.
    /// </summary>
    [Fact]
    public async Task AbsoluteVectorStorePathIsLeftAlone()
    {
        var elsewhere = Path.Combine(sandbox, "elsewhere", "vectors.db");
        WriteSavedConfig($"Data Source={elsewhere}");

        var loaded = await CreateConfigService().LoadConfigAsync();
        var afterRestart = await CreateConfigService().LoadConfigAsync();

        Assert.Equal($"Data Source={elsewhere}", loaded.VectorStore.ConnectionString);
        Assert.Equal($"Data Source={elsewhere}", afterRestart.VectorStore.ConnectionString);
    }

    /// <summary>
    /// Acceptance (3), the vector-store half, over the REAL production call site: building and
    /// initializing <see cref="TechieRagManager"/> from a saved configuration carrying the relative
    /// default must leave the working directory untouched and open the database inside the data
    /// directory. Before the fix this test creates a SQLite file in the working directory, which is
    /// what landed in the signed <c>.app</c> bundle root.
    /// </summary>
    /// <remarks>
    /// Whether the embedding provider can be reached on the test host is irrelevant — the vector
    /// store is initialized first, so the artefact is created either way. A thrown provider error is
    /// therefore swallowed: what is asserted is where the file went.
    /// </remarks>
    [Fact]
    public async Task OpeningTheRagStoreWritesNothingIntoTheWorkingDirectory()
    {
        WriteSavedConfig("Data Source=techierag.db");

        using var manager = CreateManager();
        var initialize = Task.Run(async () =>
        {
            try
            {
                await manager.InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Provider availability is not what this test is about; see the method remarks.
            }
        });

        Assert.Same(initialize, await Task.WhenAny(initialize, Task.Delay(InitializationBudget)));

        AssertWorkingDirectoryIsEmpty();
        Assert.True(
            File.Exists(Path.Combine(dataDirectory, DataDirectory.VectorDbFileName)),
            "The vector database must be created inside the data directory.");
    }

    /// <summary>
    /// Acceptance (3), the same guard with no saved configuration at all — the fresh-install path,
    /// where the relative default comes from <c>VectorStoreConfig</c> itself rather than from disk.
    /// </summary>
    [Fact]
    public async Task OpeningTheRagStoreWithNoSavedConfigWritesNothingIntoTheWorkingDirectory()
    {
        Assert.False(File.Exists(ConfigFilePath));

        using var manager = CreateManager();
        var initialize = Task.Run(async () =>
        {
            try
            {
                await manager.InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // See OpeningTheRagStoreWritesNothingIntoTheWorkingDirectory.
            }
        });

        Assert.Same(initialize, await Task.WhenAny(initialize, Task.Delay(InitializationBudget)));

        AssertWorkingDirectoryIsEmpty();
    }

    /// <summary>
    /// The REQ-FN-034 residual, over the REAL production call site: the migration console's Serilog
    /// file sink writes into the data directory's log folder, not into <c>./logs/</c> relative to
    /// wherever the console happened to be invoked from. Before the fix this leaves a
    /// <c>logs/techiedeskdb-*.log</c> in the working directory — which is why the repository root
    /// accumulated them.
    /// </summary>
    [Fact]
    public void MigrationConsoleLogsIntoTheDataDirectory()
    {
        var logFile = TechieDeskDb.Program.ConfigureLogging(dataDirectory);

        Log.Information("REQ-FN-048 working-directory guard");
        Log.CloseAndFlush();

        Assert.StartsWith(
            Path.Combine(dataDirectory, DataDirectory.LogDirectoryName), logFile, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(
            Path.Combine(dataDirectory, DataDirectory.LogDirectoryName), "techiedeskdb-*.log"));
        AssertWorkingDirectoryIsEmpty();
    }

    /// <summary>Gets the saved-configuration path inside the sandbox data directory.</summary>
    private string ConfigFilePath => Path.Combine(dataDirectory, DataDirectory.ConfigFileName);

    /// <summary>Fails with a legible message when anything at all appeared in the working directory.</summary>
    private void AssertWorkingDirectoryIsEmpty()
    {
        var strays = Directory.GetFileSystemEntries(workingDirectory);

        Assert.True(
            strays.Length == 0,
            "No persistent artefact may resolve against the working directory (REQ-FN-048), but "
            + $"these appeared: {string.Join(", ", strays.Select(Path.GetFileName))}.");
    }

    /// <summary>Builds the configuration every service under test is pointed at.</summary>
    /// <returns>A configuration naming only the sandbox data directory.</returns>
    private IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataDirectory.ConfigKey] = dataDirectory
            })
            .Build();

    /// <summary>Builds the real configuration service against the sandbox.</summary>
    /// <returns>A service with an empty cache, as at application start.</returns>
    private TechieRagConfigService CreateConfigService() =>
        new(
            CreateConfiguration(),
            new AppEnvironment(Path.Combine(sandbox, "content")),
            NullLogger<TechieRagConfigService>.Instance,
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(sandbox, "keys"))),
            NullLoggerFactory.Instance);

    /// <summary>Builds the real RAG manager against the sandbox.</summary>
    /// <returns>A manager that touches nothing outside the sandbox once it is correct.</returns>
    private TechieRagManager CreateManager() =>
        new(
            new AppEnvironment(Path.Combine(sandbox, "content")),
            NullLoggerFactory.Instance,
            NullLogger<TechieRagManager>.Instance,
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(sandbox, "keys"))),
            CreateConfiguration());

    /// <summary>
    /// Writes a saved configuration carrying the supplied vector-store connection string.
    /// </summary>
    /// <param name="connectionString">The connection string exactly as an older build left it.</param>
    /// <remarks>
    /// Literal JSON rather than a serialized <see cref="TechieRagConfig"/>, so the fixture stays a
    /// record of what is on disk in the field and does not move when the model's defaults do. The
    /// embedding source is Ollama and the LLM source is None to keep instance construction local.
    /// </remarks>
    private void WriteSavedConfig(string connectionString)
    {
        var json = $$"""
            {
              "embedding": {
                "source": 2,
                "endpoint": "http://127.0.0.1:11434",
                "apiKey": null,
                "model": "bge-m3",
                "dimensions": 1024
              },
              "vectorStore": {
                "type": 0,
                "connectionString": "{{connectionString.Replace("\\", "\\\\")}}",
                "apiKey": null
              },
              "processing": { "defaultChunkSize": 500, "defaultChunkOverlap": 50 },
              "llm": { "source": 0, "model": "", "temperature": 0.7, "maxTokens": 2048 },
              "llmFallback": null,
              "usageTracking": { "enabled": true, "alertThreshold": 0.8 },
              "resilience": { "maxRetries": 3, "timeoutSeconds": 120 },
              "rerank": { "enabled": false, "source": 0, "candidateCount": 20 }
            }
            """;

        File.WriteAllText(ConfigFilePath, json);
    }
}

/// <summary>
/// Serializes every test that changes the process working directory (REQ-FN-048).
/// </summary>
/// <remarks>
/// <see cref="Directory.SetCurrentDirectory"/> is process-global, so a test that moves it while
/// another test resolves a relative path would produce a failure that has nothing to do with either.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkingDirectoryCollection
{
    /// <summary>The collection name test classes reference.</summary>
    public const string Name = "WorkingDirectory";
}
