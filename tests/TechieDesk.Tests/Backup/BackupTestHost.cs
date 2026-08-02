using System.Text.Json;

using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using TechieDesk.Services.Backup;
using TechieDeskDb;

using Xunit;

namespace TechieDesk.Tests.Backup;

/// <summary>
/// A disposable scratch install for the backup tests (REQ-FN-046/047).
/// </summary>
/// <remarks>
/// <para>
/// Every test owns a GUID-named temp directory steered through the <c>AppDb:DataDirectory</c>
/// override, so nothing ever reads or writes the real per-user data directory. That matters more
/// here than elsewhere: these tests exercise a service whose whole job is to read the install's
/// databases, and a mistake would otherwise operate on the developer's own workspaces.
/// </para>
/// <para>
/// The host can also seed the credential-bearing artefacts a real data directory holds — the Data
/// Protection key ring, <c>connector-secrets.json</c>, encrypted API keys in
/// <c>techierag-config.json</c>, and an AppManager token in <c>LicenseCache</c>. Nothing under test
/// should ever reach them, which is exactly what
/// <see cref="CredentialExclusionTests"/> proves by scanning the produced archive.
/// </para>
/// </remarks>
internal sealed class BackupTestHost : IDisposable
{
    /// <summary>Creates a scratch install.</summary>
    /// <param name="embeddingModel">Embedding model name to record in the saved configuration.</param>
    /// <param name="dimensions">Embedding dimension to record.</param>
    internal BackupTestHost(string embeddingModel = "bge-m3", int dimensions = 1024)
    {
        Directory = Path.Combine(
            Path.GetTempPath(), "techiedesk-backup", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);

        WriteConfig(embeddingModel, dimensions);

        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataDirectory.ConfigKey] = Directory
            })
            .Build();

        Service = new BackupService(
            Configuration, NullLogger<BackupService>.Instance, "1.2.3-test");
    }

    /// <summary>Gets the scratch data directory.</summary>
    internal string Directory { get; }

    /// <summary>Gets the configuration steering the service at that directory.</summary>
    internal IConfiguration Configuration { get; }

    /// <summary>Gets the service under test.</summary>
    internal BackupService Service { get; }

    /// <summary>Gets the path of the RAG store inside this install.</summary>
    internal string RagStorePath => Path.Combine(Directory, DataDirectory.RagStoreFileName);

    /// <summary>Gets the path of the vector store inside this install.</summary>
    internal string VectorDbPath => Path.Combine(Directory, DataDirectory.VectorDbFileName);

    /// <summary>Writes the saved provider configuration recording the embedding identity.</summary>
    /// <param name="model">Model name.</param>
    /// <param name="dimensions">Vector width.</param>
    /// <param name="apiKey">Optional encrypted API key to plant, as a real install would hold.</param>
    internal void WriteConfig(string model, int dimensions, string? apiKey = null)
    {
        var config = new
        {
            embedding = new { source = 2, model, dimensions, apiKey },
            vectorStore = new { type = 0, connectionString = "Data Source=techierag.db", apiKey },
            llm = new { source = 0, model = "", apiKey }
        };

        File.WriteAllText(
            Path.Combine(Directory, DataDirectory.ConfigFileName),
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Creates the two content databases with the schema the TechieRag library uses.</summary>
    internal void CreateStores()
    {
        using var ragStore = Open(RagStorePath);
        ragStore.Execute(
            """
            CREATE TABLE IF NOT EXISTS TrWorkspace (
                WorkspaceId TEXT PRIMARY KEY, Name TEXT NOT NULL, SystemPrompt TEXT, LlmModel TEXT,
                SimilarityThreshold REAL, TopK INTEGER, RerankEnabled INTEGER NOT NULL DEFAULT 0,
                ChatMode TEXT NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS TrWorkspaceDocument (
                WorkspaceId TEXT NOT NULL, DocumentId TEXT NOT NULL, ContentHash TEXT NOT NULL,
                IsPinned INTEGER NOT NULL DEFAULT 0, AddedAt TEXT NOT NULL,
                PRIMARY KEY (WorkspaceId, DocumentId));
            CREATE TABLE IF NOT EXISTS TrThread (
                ThreadId TEXT PRIMARY KEY, UserId TEXT NOT NULL, WorkspaceId TEXT,
                Title TEXT NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS TrMessage (
                MessageId TEXT PRIMARY KEY, ThreadId TEXT NOT NULL, Role TEXT NOT NULL,
                Content TEXT, SourcesJson TEXT, CreatedAt TEXT NOT NULL);
            """);

        using var vectorDb = Open(VectorDbPath);
        vectorDb.Execute(
            """
            CREATE TABLE IF NOT EXISTS Documents (
                Id TEXT PRIMARY KEY, Name TEXT NOT NULL, SourcePath TEXT NOT NULL,
                ChunkCount INTEGER DEFAULT 0, IngestedAt TEXT NOT NULL, Metadata TEXT);
            CREATE TABLE IF NOT EXISTS Chunks (
                Id TEXT PRIMARY KEY, DocumentId TEXT NOT NULL, Text TEXT NOT NULL, Vector BLOB,
                PageNumber INTEGER, ChunkIndex INTEGER, Metadata TEXT, CreatedAt TEXT NOT NULL);
            """);
    }

    /// <summary>Seeds one workspace with a document, a chunk carrying a vector, and a conversation.</summary>
    /// <param name="workspaceId">Workspace identifier.</param>
    /// <param name="name">Workspace display name.</param>
    /// <param name="documentText">Chunk text, used by tests to identify what came back.</param>
    /// <returns>The identifier of the document created.</returns>
    internal string SeedWorkspace(string workspaceId, string name, string documentText)
    {
        var documentId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("O");

        using var ragStore = Open(RagStorePath);
        ragStore.Execute(
            """
            INSERT INTO TrWorkspace (WorkspaceId, Name, SystemPrompt, LlmModel, SimilarityThreshold,
                                     TopK, RerankEnabled, ChatMode, CreatedAt, UpdatedAt)
            VALUES (@workspaceId, @name, 'Be helpful.', 'gpt-test', 0.5, 4, 0, 'Chat', @now, @now)
            """,
            new { workspaceId, name, now });

        ragStore.Execute(
            """
            INSERT INTO TrWorkspaceDocument (WorkspaceId, DocumentId, ContentHash, IsPinned, AddedAt)
            VALUES (@workspaceId, @documentId, 'hash-1', 0, @now)
            """,
            new { workspaceId, documentId, now });

        var threadId = Guid.NewGuid().ToString();
        ragStore.Execute(
            """
            INSERT INTO TrThread (ThreadId, UserId, WorkspaceId, Title, CreatedAt, UpdatedAt)
            VALUES (@threadId, 'local', @workspaceId, @title, @now, @now)
            """,
            new { threadId, workspaceId, title = $"{name} thread", now });

        ragStore.Execute(
            """
            INSERT INTO TrMessage (MessageId, ThreadId, Role, Content, SourcesJson, CreatedAt)
            VALUES (@messageId, @threadId, 'user', @content, NULL, @now)
            """,
            new { messageId = Guid.NewGuid().ToString(), threadId, content = $"Ask about {name}", now });

        using var vectorDb = Open(VectorDbPath);
        vectorDb.Execute(
            """
            INSERT INTO Documents (Id, Name, SourcePath, ChunkCount, IngestedAt, Metadata)
            VALUES (@documentId, @docName, 'https://example.invalid/doc', 1, @now, '{}')
            """,
            new { documentId, docName = $"{name}.md", now });

        vectorDb.Execute(
            """
            INSERT INTO Chunks (Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt)
            VALUES (@chunkId, @documentId, @documentText, @vector, 1, 0, '{}', @now)
            """,
            new
            {
                chunkId = Guid.NewGuid().ToString(),
                documentId,
                documentText,
                vector = MakeVector(documentText),
                now
            });

        return documentId;
    }

    /// <summary>Plants the credential-bearing artefacts a real data directory holds.</summary>
    /// <param name="sentinels">Marker strings a correct archive must never contain.</param>
    /// <remarks>
    /// Deliberately writes them in the same places and the same shapes production uses, because a
    /// test that plants secrets somewhere the packer was never going to look proves nothing.
    /// </remarks>
    internal void SeedCredentials(params string[] sentinels)
    {
        var keyRing = Path.Combine(Directory, DataDirectory.KeyRingDirectoryName);
        System.IO.Directory.CreateDirectory(keyRing);
        File.WriteAllText(
            Path.Combine(keyRing, "key-test.xml"),
            $"<key><descriptor>{sentinels[0]}</descriptor></key>");

        File.WriteAllText(
            Path.Combine(Directory, "connector-secrets.json"),
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["techiedesk.connector.abc"] = $"enc:v1:{sentinels[0]}"
            }));

        WriteConfig("bge-m3", 1024, apiKey: $"enc:v1:{sentinels[0]}");

        var appDbPath = Path.Combine(Directory, DataDirectory.AppDbFileName);
        Assert.Equal(0, MigrationRunner.Run("Sqlite", $"Data Source={appDbPath}"));

        using var appDb = Open(appDbPath);
        appDb.Execute(
            """
            INSERT INTO "LicenseCache" ("UserId", "PayloadJson", "ValidatedAt")
            VALUES ('owner', @payload, @now)
            """,
            new
            {
                payload = $$"""{"accessToken":"{{sentinels[0]}}","refreshToken":"{{sentinels[0]}}"}""",
                now = DateTime.UtcNow.ToString("O")
            });

        appDb.Execute(
            """
            INSERT INTO "InstanceSetting" ("SettingKey", "SettingValue", "UpdatedAt")
            VALUES ('Secret.Test', @value, @now)
            """,
            new { value = sentinels[0], now = DateTime.UtcNow.ToString("O") });
    }

    /// <summary>Counts rows in a table of one of this install's databases.</summary>
    /// <param name="databasePath">Absolute path of the database file.</param>
    /// <param name="table">Table name; a literal from the test, never user input.</param>
    /// <returns>The row count, or zero when the table does not exist.</returns>
    internal static long CountRows(string databasePath, string table)
    {
        if (!File.Exists(databasePath))
        {
            return 0;
        }

        using var connection = Open(databasePath);
        try
        {
            return connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table}");
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    /// <summary>Reads every chunk's text out of a vector store.</summary>
    /// <param name="databasePath">Absolute path of the vector database.</param>
    /// <returns>The chunk texts present.</returns>
    internal static IReadOnlyList<string> ChunkTexts(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return [];
        }

        using var connection = Open(databasePath);
        try
        {
            return connection.Query<string>("SELECT Text FROM Chunks ORDER BY Text").ToList();
        }
        catch (SqliteException)
        {
            return [];
        }
    }

    /// <summary>Reads the vector stored against a chunk text.</summary>
    /// <param name="databasePath">Absolute path of the vector database.</param>
    /// <param name="text">The chunk text to look up.</param>
    /// <returns>The stored vector, or null when the chunk has none.</returns>
    internal static byte[]? VectorFor(string databasePath, string text)
    {
        using var connection = Open(databasePath);
        return connection.QueryFirstOrDefault<byte[]?>(
            "SELECT Vector FROM Chunks WHERE Text = @text", new { text });
    }

    /// <summary>Reads workspace names out of a RAG store.</summary>
    /// <param name="databasePath">Absolute path of the RAG store.</param>
    /// <returns>The workspace names present, ordered.</returns>
    internal static IReadOnlyList<string> WorkspaceNames(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return [];
        }

        using var connection = Open(databasePath);
        try
        {
            return connection.Query<string>("SELECT Name FROM TrWorkspace ORDER BY Name").ToList();
        }
        catch (SqliteException)
        {
            return [];
        }
    }

    /// <summary>Builds a deterministic pseudo-embedding so tests can recognise it after a round trip.</summary>
    /// <param name="seed">Text to derive the vector from.</param>
    /// <returns>A byte payload standing in for a float vector.</returns>
    internal static byte[] MakeVector(string seed)
    {
        var vector = new byte[64];
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (byte)((seed.Length * 31 + index * 7) % 251);
        }

        return vector;
    }

    /// <summary>Opens a read-write connection to a scratch database.</summary>
    /// <param name="path">Absolute database path.</param>
    /// <returns>An open connection.</returns>
    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        connection.Open();
        return connection;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must never fail a run.
        }
    }
}
