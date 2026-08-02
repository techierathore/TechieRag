namespace TechieDesk.Services.Backup;

/// <summary>
/// Idempotent DDL for the two content databases a restore writes into (REQ-FN-046).
/// </summary>
/// <remarks>
/// <para>
/// These statements mirror, verbatim, the <c>CREATE TABLE IF NOT EXISTS</c> DDL the TechieRag
/// library issues at runtime from <c>RelationalWorkspaceStore</c>, <c>RelationalConversationStore</c>
/// and <c>SqliteVecStore</c>. They are restated here for one reason: restoring into a FRESH install
/// is the headline use case — a colleague receives a <c>.tdbak</c> and opens it on a machine where
/// TechieDesk has never ingested anything — and on that machine the library has not yet created its
/// tables, because it creates them lazily on first use.
/// </para>
/// <para>
/// Restating DDL is normally a duplication smell. It is accepted here because every statement is
/// <c>IF NOT EXISTS</c>, so running them can only ever be a no-op against an install the library has
/// already initialised, and because the alternative — booting the whole TechieRag stack just to
/// create four tables — would drag the embedding provider and its model download into a restore.
/// </para>
/// </remarks>
internal static class BackupSchema
{
    /// <summary>Statements creating the workspace/conversation store, in dependency order.</summary>
    internal static IReadOnlyList<string> RagStoreStatements { get; } =
    [
        """
        CREATE TABLE IF NOT EXISTS TrWorkspace (
            WorkspaceId TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            SystemPrompt TEXT,
            LlmModel TEXT,
            SimilarityThreshold REAL,
            TopK INTEGER,
            RerankEnabled INTEGER NOT NULL DEFAULT 0,
            ChatMode TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS TrWorkspaceDocument (
            WorkspaceId TEXT NOT NULL,
            DocumentId TEXT NOT NULL,
            ContentHash TEXT NOT NULL,
            IsPinned INTEGER NOT NULL DEFAULT 0,
            AddedAt TEXT NOT NULL,
            PRIMARY KEY (WorkspaceId, DocumentId)
        )
        """,
        "CREATE INDEX IF NOT EXISTS IxTrWorkspaceDocumentContentHash ON TrWorkspaceDocument(ContentHash)",
        """
        CREATE TABLE IF NOT EXISTS TrThread (
            ThreadId TEXT PRIMARY KEY,
            UserId TEXT NOT NULL,
            WorkspaceId TEXT,
            Title TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS TrMessage (
            MessageId TEXT PRIMARY KEY,
            ThreadId TEXT NOT NULL,
            Role TEXT NOT NULL,
            Content TEXT,
            SourcesJson TEXT,
            CreatedAt TEXT NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS IxTrThreadUserId ON TrThread(UserId)",
        "CREATE INDEX IF NOT EXISTS IxTrMessageThreadId ON TrMessage(ThreadId)"
    ];

    /// <summary>Statements creating the vector store's catalogue and chunk tables.</summary>
    internal static IReadOnlyList<string> VectorStoreStatements { get; } =
    [
        """
        CREATE TABLE IF NOT EXISTS Documents (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            SourcePath TEXT NOT NULL,
            ChunkCount INTEGER DEFAULT 0,
            IngestedAt TEXT NOT NULL,
            Metadata TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS Chunks (
            Id TEXT PRIMARY KEY,
            DocumentId TEXT NOT NULL,
            Text TEXT NOT NULL,
            Vector BLOB,
            PageNumber INTEGER,
            ChunkIndex INTEGER,
            Metadata TEXT,
            CreatedAt TEXT NOT NULL,
            FOREIGN KEY (DocumentId) REFERENCES Documents(Id) ON DELETE CASCADE
        )
        """,
        "CREATE INDEX IF NOT EXISTS IdxChunksDocument ON Chunks(DocumentId)"
    ];
}
