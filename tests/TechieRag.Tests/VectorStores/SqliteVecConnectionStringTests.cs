using Microsoft.Data.Sqlite;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.VectorStores;

/// <summary>
/// REQ-RAG-167 (TrStudio TR-RAG-005): <c>UseSqliteVec</c> takes a file path or a connection string, and the AI
/// reference says what the store is.
/// </summary>
/// <remarks>
/// TrStudio's repro on 1.1.5: <c>UseSqliteVec("Data Source=" + path)</c> threw
/// <c>SqliteException: SQLite Error 14: 'unable to open database file'</c> on the first call, because the builder
/// wrapped the value in a second <c>Data Source=</c>; the bare path worked. The reference showed the failing form
/// (<c>.UseSqliteVec("Data Source=app.db")</c>) and called the store "SQLite with vec extension".
/// </remarks>
public sealed class SqliteVecConnectionStringTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "trsqlite-" + Guid.NewGuid().ToString("N"));

    /// <summary>Initializes a new instance of the <see cref="SqliteVecConnectionStringTests"/> class.</summary>
    public SqliteVecConnectionStringTests() => Directory.CreateDirectory(folder);

    /// <summary>
    /// TrStudio's repro with the connection-string form: the store opens the named file, lists no documents, and
    /// the file holds the <c>Documents</c> and <c>Chunks</c> tables.
    /// </summary>
    /// <returns>A task that completes when the store has been read.</returns>
    [Fact(DisplayName = "REQ-RAG-167 ConnectionStringFormOpensTheNamedFile")]
    public async Task ConnectionStringFormOpensTheNamedFile()
    {
        var path = Path.Combine(folder, "TrStudio.vectors.db");

        var documents = await BuildAndListAsync("Data Source=" + path);

        Assert.Empty(documents);
        Assert.Equal(["Chunks", "Documents"], TablesIn(path));
    }

    /// <summary>The bare path keeps working exactly as before, and a path holding '=' is still a path.</summary>
    /// <returns>A task that completes when both stores have been read.</returns>
    [Fact(DisplayName = "REQ-RAG-167 BarePathStillWorks")]
    public async Task BarePathStillWorks()
    {
        var path = Path.Combine(folder, "plain.db");
        var oddPath = Path.Combine(folder, "a=b.db");

        Assert.Empty(await BuildAndListAsync(path));
        Assert.Empty(await BuildAndListAsync(oddPath));
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(oddPath));
    }

    /// <summary>
    /// The shipped AI reference names the argument a path or connection string, names both tables, and no longer
    /// promises a vec extension.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-167 ReferenceDescribesTheStoreAsShipped")]
    public void ReferenceDescribesTheStoreAsShipped()
    {
        foreach (var file in new[] { "docs/TechieRag-AI-Reference.md", "src/TechieRag/build/content/TechieRag-AI-Reference.md" })
        {
            var row = LibraryRepoFiles.Read(file).Split('\n').Single(line => line.StartsWith("| `UseSqliteVec("));

            Assert.Contains("`UseSqliteVec(databasePath)`", row);
            Assert.Contains("\"Data Source=app.db\"", row);
            Assert.Contains("`Documents`", row);
            Assert.Contains("`Chunks`", row);
            Assert.DoesNotContain("vec extension", row);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still held is left in the temp folder.
        }
    }

    private static async Task<IReadOnlyList<TechieRag.Models.Document>> BuildAndListAsync(string databasePathOrConnectionString)
    {
        var rag = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new FakeEmbeddingProvider())
            .UseSqliteVec(databasePathOrConnectionString)
            .Build();
        await rag.InitializeAsync();
        return await rag.ListDocumentsAsync();
    }

    private static List<string> TablesIn(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
