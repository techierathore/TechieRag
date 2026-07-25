using System.Data.Common;
using System.Text.Json;
using Dapper;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Persistence;

/// <summary>
/// Shared Dapper-based implementation of <see cref="IConversationStore"/> for relational
/// databases. Owns and self-creates the TrThread and TrMessage tables.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Contains all SQL and mapping logic shared by the SQLite and
/// PostgreSQL conversation stores; subclasses only supply the connection.</para>
/// <para><b>Schema:</b> Idempotent <c>CREATE TABLE IF NOT EXISTS</c> statements using
/// PascalCase singular names with no underscores, portable across SQLite and PostgreSQL.
/// Timestamps are stored as ISO-8601 text for portability.</para>
/// </remarks>
public abstract class RelationalConversationStore : IConversationStore
{
    private static readonly JsonSerializerOptions SourcesJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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
                CREATE TABLE IF NOT EXISTS TrThread (
                    ThreadId TEXT PRIMARY KEY,
                    UserId TEXT NOT NULL,
                    WorkspaceId TEXT,
                    Title TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                )
                """).ConfigureAwait(false);

            await connection.ExecuteAsync("""
                CREATE TABLE IF NOT EXISTS TrMessage (
                    MessageId TEXT PRIMARY KEY,
                    ThreadId TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    Content TEXT,
                    SourcesJson TEXT,
                    CreatedAt TEXT NOT NULL
                )
                """).ConfigureAwait(false);

            await connection.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IxTrThreadUserId ON TrThread(UserId)").ConfigureAwait(false);
            await connection.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IxTrMessageThreadId ON TrMessage(ThreadId)").ConfigureAwait(false);

            initialized = true;
        }
        finally
        {
            initLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<ConversationThread> CreateThreadAsync(
        string userId,
        string? workspaceId = null,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var thread = new ConversationThread
        {
            UserId = userId,
            WorkspaceId = workspaceId,
            Title = string.IsNullOrWhiteSpace(title) ? "New Conversation" : title
        };

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync("""
            INSERT INTO TrThread (ThreadId, UserId, WorkspaceId, Title, CreatedAt, UpdatedAt)
            VALUES (@ThreadId, @UserId, @WorkspaceId, @Title, @CreatedAt, @UpdatedAt)
            """,
            new
            {
                thread.ThreadId,
                thread.UserId,
                thread.WorkspaceId,
                thread.Title,
                CreatedAt = thread.CreatedAt.ToString("o"),
                UpdatedAt = thread.UpdatedAt.ToString("o")
            }).ConfigureAwait(false);

        return thread;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConversationThread>> ListThreadsAsync(
        string userId,
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var sql = workspaceId is null
            ? "SELECT ThreadId, UserId, WorkspaceId, Title, CreatedAt, UpdatedAt FROM TrThread WHERE UserId = @UserId ORDER BY UpdatedAt DESC"
            : "SELECT ThreadId, UserId, WorkspaceId, Title, CreatedAt, UpdatedAt FROM TrThread WHERE UserId = @UserId AND WorkspaceId = @WorkspaceId ORDER BY UpdatedAt DESC";

        var rows = await connection.QueryAsync<ThreadRow>(sql, new { UserId = userId, WorkspaceId = workspaceId })
            .ConfigureAwait(false);
        return rows.Select(MapThread).ToList();
    }

    /// <inheritdoc/>
    public async Task<ConversationThread?> GetThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(threadId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<ThreadRow>(
            "SELECT ThreadId, UserId, WorkspaceId, Title, CreatedAt, UpdatedAt FROM TrThread WHERE ThreadId = @ThreadId",
            new { ThreadId = threadId }).ConfigureAwait(false);

        return row is null ? null : MapThread(row);
    }

    /// <inheritdoc/>
    public async Task RenameThreadAsync(string threadId, string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(threadId);
        ArgumentException.ThrowIfNullOrEmpty(title);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "UPDATE TrThread SET Title = @Title, UpdatedAt = @UpdatedAt WHERE ThreadId = @ThreadId",
            new { ThreadId = threadId, Title = title, UpdatedAt = DateTime.UtcNow.ToString("o") })
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(threadId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "DELETE FROM TrMessage WHERE ThreadId = @ThreadId", new { ThreadId = threadId }).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "DELETE FROM TrThread WHERE ThreadId = @ThreadId", new { ThreadId = threadId }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAllForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "DELETE FROM TrMessage WHERE ThreadId IN (SELECT ThreadId FROM TrThread WHERE UserId = @UserId)",
            new { UserId = userId }).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "DELETE FROM TrThread WHERE UserId = @UserId", new { UserId = userId }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<StoredChatMessage> AddMessageAsync(
        string threadId,
        ChatMessage message,
        IReadOnlyList<SearchResult>? sources = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(threadId);
        ArgumentNullException.ThrowIfNull(message);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var stored = new StoredChatMessage
        {
            ThreadId = threadId,
            Role = message.Role,
            Content = message.Content,
            Sources = sources
        };

        var sourcesJson = sources is { Count: > 0 }
            ? JsonSerializer.Serialize(sources, SourcesJsonOptions)
            : null;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync("""
            INSERT INTO TrMessage (MessageId, ThreadId, Role, Content, SourcesJson, CreatedAt)
            VALUES (@MessageId, @ThreadId, @Role, @Content, @SourcesJson, @CreatedAt)
            """,
            new
            {
                stored.MessageId,
                stored.ThreadId,
                stored.Role,
                stored.Content,
                SourcesJson = sourcesJson,
                CreatedAt = stored.CreatedAt.ToString("o")
            }).ConfigureAwait(false);

        await connection.ExecuteAsync(
            "UPDATE TrThread SET UpdatedAt = @UpdatedAt WHERE ThreadId = @ThreadId",
            new { ThreadId = threadId, UpdatedAt = DateTime.UtcNow.ToString("o") }).ConfigureAwait(false);

        return stored;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StoredChatMessage>> GetMessagesAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(threadId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<MessageRow>(
            "SELECT MessageId, ThreadId, Role, Content, SourcesJson, CreatedAt FROM TrMessage WHERE ThreadId = @ThreadId ORDER BY CreatedAt, MessageId",
            new { ThreadId = threadId }).ConfigureAwait(false);

        return rows.Select(MapMessage).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChatMessage>> GetTrimmedHistoryAsync(
        string threadId,
        int maxTokens,
        Func<string, int> tokenCounter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);

        var messages = await GetMessagesAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (messages.Count == 0) return [];

        var result = new List<ChatMessage>();
        StoredChatMessage? systemMessage = null;
        var totalTokens = 0;

        if (messages[0].Role == "system")
        {
            systemMessage = messages[0];
            totalTokens += tokenCounter(systemMessage.Content ?? string.Empty);
        }

        var nonSystem = systemMessage is not null ? messages.Skip(1).ToList() : messages.ToList();

        for (var i = nonSystem.Count - 1; i >= 0; i--)
        {
            var messageTokens = tokenCounter(nonSystem[i].Content ?? string.Empty);
            if (totalTokens + messageTokens > maxTokens) break;
            totalTokens += messageTokens;
            result.Insert(0, nonSystem[i].ToChatMessage());
        }

        if (systemMessage is not null)
        {
            result.Insert(0, systemMessage.ToChatMessage());
        }

        return result;
    }

    private static ConversationThread MapThread(ThreadRow row) => new()
    {
        ThreadId = row.ThreadId,
        UserId = row.UserId,
        WorkspaceId = row.WorkspaceId,
        Title = row.Title,
        CreatedAt = ParseDate(row.CreatedAt),
        UpdatedAt = ParseDate(row.UpdatedAt)
    };

    private static StoredChatMessage MapMessage(MessageRow row) => new()
    {
        MessageId = row.MessageId,
        ThreadId = row.ThreadId,
        Role = row.Role,
        Content = row.Content,
        Sources = row.SourcesJson is null
            ? null
            : JsonSerializer.Deserialize<List<SearchResult>>(row.SourcesJson, SourcesJsonOptions),
        CreatedAt = ParseDate(row.CreatedAt)
    };

    private static DateTime ParseDate(string value) =>
        DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    private sealed class ThreadRow
    {
        public string ThreadId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string? WorkspaceId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }

    private sealed class MessageRow
    {
        public string MessageId { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string? Content { get; set; }
        public string? SourcesJson { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }
}
