using TechieRag.Tests.VectorStores.Live;
using Xunit;

namespace TechieRag.Tests.VectorStores;

/// <summary>
/// The one-call PostgreSQL builder and the live-test settings file, checked without a server
/// (REQ-RAG-152 / BRD-210). The live half is <see cref="LivePostgresEndToEndTests"/>.
/// </summary>
public sealed class UsePostgresTests
{
    private const string Connection = "Host=db.example;Database=app;Username=u;Password=p";

    /// <summary>
    /// <c>UsePostgres(connectionString)</c> points the vector store and the persistence stores at the same
    /// connection string, so vectors, conversations and workspaces share one database.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-152 UsePostgresPointsVectorsAndStoresAtOneDatabase")]
    public void UsePostgresPointsVectorsAndStoresAtOneDatabase()
    {
        var config = new TechieRagBuilder().UsePostgres(Connection, defaultUserId: "alice").GetConfig();

        Assert.Equal(VectorStoreType.PgVector, config.VectorStore.Type);
        Assert.Equal(Connection, config.VectorStore.ConnectionString);
        Assert.Equal(StoreProvider.Postgres, config.Persistence.Provider);
        Assert.Equal(Connection, config.Persistence.ConnectionString);
        Assert.Equal("alice", config.Persistence.DefaultUserId);
    }

    /// <summary>An empty connection string is refused at the call, not at the first query.</summary>
    [Fact]
    public void UsePostgresRefusesAnEmptyConnectionString()
    {
        Assert.Throws<ArgumentException>(() => new TechieRagBuilder().UsePostgres(string.Empty));
    }

    /// <summary>
    /// The settings file's <c>Postgres:ConnectionString</c> is read; a file without one gives null, so the
    /// live tests skip rather than fail.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-152 LiveSettingsFileSuppliesTheConnectionString")]
    public void LiveSettingsFileSuppliesTheConnectionString()
    {
        Assert.Equal(Connection, LiveTestSettings.ParsePostgres($$"""{ "Postgres": { "ConnectionString": "{{Connection}}" } }"""));
        Assert.Null(LiveTestSettings.ParsePostgres("""{ "Postgres": { "ConnectionString": "" } }"""));
        Assert.Null(LiveTestSettings.ParsePostgres("{}"));
    }

    /// <summary>The settings file is found by walking up from the test binaries to the folder that holds it.</summary>
    [Fact]
    public void LiveSettingsFileIsFoundAboveTheBinaries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"techierag-live-{Guid.NewGuid():N}");
        var binaries = Directory.CreateDirectory(Path.Combine(root, "bin", "Debug", "net10.0")).FullName;
        try
        {
            var settings = Path.Combine(root, LiveTestSettings.FileName);
            File.WriteAllText(settings, "{}");

            Assert.Equal(settings, LiveTestSettings.FindSettingsFile(binaries));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The committed example beside the tests has the shape the reader expects.</summary>
    [Fact]
    public void CommittedExampleHasTheExpectedShape()
    {
        var example = FindExample();

        Assert.True(File.Exists(example), $"missing {example}");
        Assert.NotNull(LiveTestSettings.ParsePostgres(File.ReadAllText(example)));
    }

    /// <summary>Finds the committed example by walking up from the binaries.</summary>
    /// <returns>The example's path.</returns>
    private static string FindExample()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "livetests.settings.example.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "livetests.settings.example.json";
    }
}
