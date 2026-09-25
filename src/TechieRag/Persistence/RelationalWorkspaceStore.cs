using System.Data.Common;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Persistence;

/// <summary>
/// Shared plain ADO.NET implementation of <see cref="IWorkspaceStore"/> for relational
/// databases. Owns and self-creates the TrWorkspace and TrWorkspaceDocument tables.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Contains all SQL and mapping logic shared by the SQLite and
/// PostgreSQL workspace stores; subclasses only supply the connection.</para>
/// <para><b>Schema:</b> Idempotent <c>CREATE TABLE IF NOT EXISTS</c> statements using
/// PascalCase singular names with no underscores, portable across SQLite and PostgreSQL.</para>
/// <para><b>No Reflection.Emit (REQ-FN-056).</b> Parameters and rows are mapped by hand through
/// <see cref="DbCommandExtensions"/>, so the store works in an ahead-of-time compiled iOS or
/// Mac Catalyst Release build.</para>
/// </remarks>
public abstract class RelationalWorkspaceStore : IWorkspaceStore
{
    private bool initialized;
    private readonly SemaphoreSlim initLock = new(1, 1);

    /// <summary>
    /// Creates and opens a database connection.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An open connection owned by the caller.</returns>
    protected abstract Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken);

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;

        await initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized) return;

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync("""
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
                """, cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync("""
                CREATE TABLE IF NOT EXISTS TrWorkspaceDocument (
                    WorkspaceId TEXT NOT NULL,
                    DocumentId TEXT NOT NULL,
                    ContentHash TEXT NOT NULL,
                    IsPinned INTEGER NOT NULL DEFAULT 0,
                    AddedAt TEXT NOT NULL,
                    PRIMARY KEY (WorkspaceId, DocumentId)
                )
                """, cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IxTrWorkspaceDocumentContentHash ON TrWorkspaceDocument(ContentHash)",
                cancellationToken).ConfigureAwait(false);

            initialized = true;
        }
        finally
        {
            initLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<Workspace> CreateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync("""
            INSERT INTO TrWorkspace (WorkspaceId, Name, SystemPrompt, LlmModel, SimilarityThreshold, TopK, RerankEnabled, ChatMode, CreatedAt, UpdatedAt)
            VALUES (@WorkspaceId, @Name, @SystemPrompt, @LlmModel, @SimilarityThreshold, @TopK, @RerankEnabled, @ChatMode, @CreatedAt, @UpdatedAt)
            """, cancellationToken, ToParameters(workspace)).ConfigureAwait(false);

        return workspace;
    }

    /// <inheritdoc/>
    public async Task<Workspace?> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync(
            "SELECT WorkspaceId, Name, SystemPrompt, LlmModel, SimilarityThreshold, TopK, RerankEnabled, ChatMode, CreatedAt, UpdatedAt FROM TrWorkspace WHERE WorkspaceId = @WorkspaceId",
            WorkspaceRow.Read,
            cancellationToken,
            DbParam.Text("WorkspaceId", workspaceId)).ConfigureAwait(false);

        return row is null ? null : MapWorkspace(row);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync(
            "SELECT WorkspaceId, Name, SystemPrompt, LlmModel, SimilarityThreshold, TopK, RerankEnabled, ChatMode, CreatedAt, UpdatedAt FROM TrWorkspace ORDER BY UpdatedAt DESC",
            WorkspaceRow.Read,
            cancellationToken).ConfigureAwait(false);

        return rows.Select(MapWorkspace).ToList();
    }

    /// <inheritdoc/>
    public async Task UpdateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        workspace.UpdatedAt = DateTime.UtcNow;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync("""
            UPDATE TrWorkspace
            SET Name = @Name, SystemPrompt = @SystemPrompt, LlmModel = @LlmModel,
                SimilarityThreshold = @SimilarityThreshold, TopK = @TopK,
                RerankEnabled = @RerankEnabled, ChatMode = @ChatMode, UpdatedAt = @UpdatedAt
            WHERE WorkspaceId = @WorkspaceId
            """, cancellationToken, ToParameters(workspace)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "DELETE FROM TrWorkspaceDocument WHERE WorkspaceId = @WorkspaceId",
            cancellationToken,
            DbParam.Text("WorkspaceId", workspaceId)).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "DELETE FROM TrWorkspace WHERE WorkspaceId = @WorkspaceId",
            cancellationToken,
            DbParam.Text("WorkspaceId", workspaceId)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task AddDocumentAsync(WorkspaceDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync("""
            INSERT INTO TrWorkspaceDocument (WorkspaceId, DocumentId, ContentHash, IsPinned, AddedAt)
            VALUES (@WorkspaceId, @DocumentId, @ContentHash, @IsPinned, @AddedAt)
            ON CONFLICT (WorkspaceId, DocumentId)
            DO UPDATE SET ContentHash = excluded.ContentHash, IsPinned = excluded.IsPinned
            """,
            cancellationToken,
            DbParam.Text("WorkspaceId", document.WorkspaceId),
            DbParam.Text("DocumentId", document.DocumentId),
            DbParam.Text("ContentHash", document.ContentHash),
            DbParam.Int32("IsPinned", document.IsPinned ? 1 : 0),
            DbParam.Text("AddedAt", document.AddedAt.ToString("o"))).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveDocumentAsync(string workspaceId, string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "DELETE FROM TrWorkspaceDocument WHERE WorkspaceId = @WorkspaceId AND DocumentId = @DocumentId",
            cancellationToken,
            DbParam.Text("WorkspaceId", workspaceId),
            DbParam.Text("DocumentId", documentId)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveDocumentFromAllWorkspacesAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "DELETE FROM TrWorkspaceDocument WHERE DocumentId = @DocumentId",
            cancellationToken,
            DbParam.Text("DocumentId", documentId)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkspaceDocument>> ListDocumentsAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync(
            "SELECT WorkspaceId, DocumentId, ContentHash, IsPinned, AddedAt FROM TrWorkspaceDocument WHERE WorkspaceId = @WorkspaceId ORDER BY AddedAt",
            WorkspaceDocumentRow.Read,
            cancellationToken,
            DbParam.Text("WorkspaceId", workspaceId)).ConfigureAwait(false);

        return rows.Select(MapDocument).ToList();
    }

    /// <inheritdoc/>
    public async Task<string?> FindDocumentIdByHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentHash);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var documentId = await connection.ScalarAsync(
            "SELECT DocumentId FROM TrWorkspaceDocument WHERE ContentHash = @ContentHash ORDER BY AddedAt LIMIT 1",
            null,
            cancellationToken,
            DbParam.Text("ContentHash", contentHash)).ConfigureAwait(false);
        return documentId as string;
    }

    /// <inheritdoc/>
    public async Task SetPinnedAsync(
        string workspaceId,
        string documentId,
        bool pinned,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "UPDATE TrWorkspaceDocument SET IsPinned = @IsPinned WHERE WorkspaceId = @WorkspaceId AND DocumentId = @DocumentId",
            cancellationToken,
            DbParam.Text("WorkspaceId", workspaceId),
            DbParam.Text("DocumentId", documentId),
            DbParam.Int32("IsPinned", pinned ? 1 : 0)).ConfigureAwait(false);
    }

    private static DbParam[] ToParameters(Workspace workspace) =>
    [
        DbParam.Text("WorkspaceId", workspace.WorkspaceId),
        DbParam.Text("Name", workspace.Name),
        DbParam.Text("SystemPrompt", workspace.SystemPrompt),
        DbParam.Text("LlmModel", workspace.LlmModel),
        DbParam.Double("SimilarityThreshold", workspace.SimilarityThreshold),
        DbParam.Int32("TopK", workspace.TopK),
        DbParam.Int32("RerankEnabled", workspace.RerankEnabled ? 1 : 0),
        DbParam.Text("ChatMode", workspace.ChatMode.ToString()),
        DbParam.Text("CreatedAt", workspace.CreatedAt.ToString("o")),
        DbParam.Text("UpdatedAt", workspace.UpdatedAt.ToString("o"))
    ];

    private static Workspace MapWorkspace(WorkspaceRow row) => new()
    {
        WorkspaceId = row.WorkspaceId,
        Name = row.Name,
        SystemPrompt = row.SystemPrompt,
        LlmModel = row.LlmModel,
        SimilarityThreshold = row.SimilarityThreshold is null ? null : (float)row.SimilarityThreshold.Value,
        TopK = row.TopK is null ? null : (int)row.TopK.Value,
        RerankEnabled = row.RerankEnabled != 0,
        ChatMode = Enum.TryParse<WorkspaceChatMode>(row.ChatMode, out var mode) ? mode : WorkspaceChatMode.Chat,
        CreatedAt = ParseDate(row.CreatedAt),
        UpdatedAt = ParseDate(row.UpdatedAt)
    };

    private static WorkspaceDocument MapDocument(WorkspaceDocumentRow row) => new()
    {
        WorkspaceId = row.WorkspaceId,
        DocumentId = row.DocumentId,
        ContentHash = row.ContentHash,
        IsPinned = row.IsPinned != 0,
        AddedAt = ParseDate(row.AddedAt)
    };

    private static DateTime ParseDate(string value) =>
        DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    private sealed class WorkspaceRow
    {
        public string WorkspaceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? SystemPrompt { get; set; }
        public string? LlmModel { get; set; }
        public double? SimilarityThreshold { get; set; }
        public long? TopK { get; set; }
        public long RerankEnabled { get; set; }
        public string ChatMode { get; set; } = nameof(WorkspaceChatMode.Chat);
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;

        /// <summary>Maps the current row of a query selecting the columns in declaration order.</summary>
        public static WorkspaceRow Read(DbDataReader reader) => new()
        {
            WorkspaceId = reader.GetNullableString(0) ?? string.Empty,
            Name = reader.GetNullableString(1) ?? string.Empty,
            SystemPrompt = reader.GetNullableString(2),
            LlmModel = reader.GetNullableString(3),
            SimilarityThreshold = reader.GetNullableDouble(4),
            TopK = reader.GetNullableInt64(5),
            RerankEnabled = reader.GetNullableInt64(6) ?? 0,
            ChatMode = reader.GetNullableString(7) ?? nameof(WorkspaceChatMode.Chat),
            CreatedAt = reader.GetNullableString(8) ?? string.Empty,
            UpdatedAt = reader.GetNullableString(9) ?? string.Empty
        };
    }

    private sealed class WorkspaceDocumentRow
    {
        public string WorkspaceId { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public string ContentHash { get; set; } = string.Empty;
        public long IsPinned { get; set; }
        public string AddedAt { get; set; } = string.Empty;

        /// <summary>Maps the current row of a query selecting the columns in declaration order.</summary>
        public static WorkspaceDocumentRow Read(DbDataReader reader) => new()
        {
            WorkspaceId = reader.GetNullableString(0) ?? string.Empty,
            DocumentId = reader.GetNullableString(1) ?? string.Empty,
            ContentHash = reader.GetNullableString(2) ?? string.Empty,
            IsPinned = reader.GetNullableInt64(3) ?? 0,
            AddedAt = reader.GetNullableString(4) ?? string.Empty
        };
    }
}
