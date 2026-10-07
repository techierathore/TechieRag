using TechieRag.Models;
using TechieRag.Tests.Connectors.Email;
using TechieRag.Tests.Embedding;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Persistence;

/// <summary>
/// Where the SQLite database goes when an app names no folder (REQ-RAG-122 / BRD-182, Sevak feedback
/// TR-RAG-040; owner decision 2026-10-06: warn and keep existing files, never move them).
/// </summary>
/// <remarks>
/// Runs in the model-root collection: it moves the process-wide model and data roots, which the
/// model-root tests also read.
/// </remarks>
[Collection(ModelRootCollection.Name)]
public sealed class DefaultDatabaseLocationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"trdataroot-{Guid.NewGuid():N}");

    /// <summary>
    /// A new app builds the library naming no database folder: the database is created under the
    /// TechieRag data folder for that app, beside the models and following the models' override.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-122 NewAppDatabaseGoesToPerUserDataFolder")]
    public async Task NewAppDatabaseGoesToPerUserDataFolder()
    {
        Assert.False(File.Exists(Path.Combine(Environment.CurrentDirectory, DataRoot.DefaultDatabaseFileName)),
            "Precondition: the test run folder must not hold a legacy techierag.db.");
        ModelRoot.Set(Path.Combine(root, "TechieRag", "models"));
        DataRoot.Set(null);
        DataRoot.SetAppName("NewApp");

        var rag = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new FakeEmbeddingProvider())
            .UseSqliteVec()
            .WithPersistence(StoreProvider.Sqlite)
            .Build();
        await rag.InitializeAsync();
        await rag.IngestTextAsync("hello", "hello.txt");

        var expected = Path.Combine(root, "TechieRag", "data", "NewApp", "techierag.db");
        Assert.Equal(expected, DataRoot.DefaultDatabasePath);
        Assert.True(File.Exists(expected));
        Assert.Single(await rag.ListDocumentsAsync());
    }

    /// <summary>
    /// With no override at all the root is the per-user default, <c>&lt;LocalApplicationData&gt;/TechieRag/data</c>,
    /// the sibling of the models folder, and the public property returns the app's file in it.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-122 DefaultRootIsPerUserTechieRagData")]
    public void DefaultRootIsPerUserTechieRagData()
    {
        ModelRoot.Set(null);
        DataRoot.Set(null);
        DataRoot.SetAppName("SomeApp");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(Path.Combine(localAppData, "TechieRag", "data"), DataRoot.DefaultPath);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ModelRoot.EnvironmentVariable)))
        {
            Assert.Equal(Path.Combine(localAppData, "TechieRag", "data", "SomeApp"), DataRoot.AppDirectory);
        }
    }

    /// <summary>A folder set in code wins over the model root, and each app keeps its own sub-folder.</summary>
    [Fact(DisplayName = "REQ-RAG-122 CodeOverrideWinsAndAppsNeverShare")]
    public void CodeOverrideWinsAndAppsNeverShare()
    {
        ModelRoot.Set(Path.Combine(root, "elsewhere", "models"));
        DataRoot.Set(Path.Combine(root, "databases"));

        DataRoot.SetAppName("AppOne");
        var first = DataRoot.ResolveDatabasePath(root, null, createDirectory: false);
        DataRoot.SetAppName("AppTwo");
        var second = DataRoot.ResolveDatabasePath(root, null, createDirectory: false);

        Assert.Equal(Path.Combine(root, "databases", "AppOne", "techierag.db"), first);
        Assert.Equal(Path.Combine(root, "databases", "AppTwo", "techierag.db"), second);
    }

    /// <summary>
    /// An existing <c>techierag.db</c> in the folder the app runs from is used where it is, never
    /// moved, and a warning names that folder.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-122 ExistingRunFolderDatabaseIsKeptWithWarning")]
    public void ExistingRunFolderDatabaseIsKeptWithWarning()
    {
        var runFolder = Path.Combine(root, "run");
        Directory.CreateDirectory(runFolder);
        var legacy = Path.Combine(runFolder, "techierag.db");
        File.WriteAllText(legacy, "existing");
        DataRoot.Set(Path.Combine(root, "data"));
        var logger = new RecordingLogger<DefaultDatabaseLocationTests>();

        var resolved = DataRoot.ResolveDatabasePath(runFolder, logger, createDirectory: true);

        Assert.Equal(legacy, resolved);
        Assert.Equal("existing", File.ReadAllText(legacy));
        Assert.False(Directory.Exists(Path.Combine(root, "data")));
        Assert.Contains(logger.Messages, m => m.Contains(runFolder, StringComparison.Ordinal));
    }

    /// <summary>A path the caller set explicitly, relative or absolute, is used exactly as given.</summary>
    [Fact(DisplayName = "REQ-RAG-122 ExplicitPathStaysAsGiven")]
    public void ExplicitPathStaysAsGiven()
    {
        var builder = new TechieRagBuilder().UseSqliteVec("relative/my.db");

        Assert.Equal("Data Source=relative/my.db", builder.GetConfig().VectorStore.ConnectionString);
        Assert.True(builder.GetConfig().VectorStore.IsConnectionStringSet);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ModelRoot.Set(null);
        DataRoot.Set(null);
        DataRoot.SetAppName(null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
