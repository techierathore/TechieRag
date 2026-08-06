using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Database-backed implementation of <see cref="IConversationMemory"/> that persists
/// the current conversation into an <see cref="IConversationStore"/> thread.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Drop-in persistent replacement for InMemoryConversationMemory:
/// history survives application restarts. Each conversation maps to a TrThread row and its
/// messages to TrMessage rows.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when both WithConversationMemory and
/// WithPersistence are configured, or constructed directly around any IConversationStore.</para>
/// </remarks>
public class DbConversationMemory : IConversationMemory
{
    private readonly IConversationStore store;
    private readonly string userId;
    private readonly SemaphoreSlim threadLock = new(1, 1);
    private string? threadId;

    /// <summary>
    /// Creates a new database-backed conversation memory.
    /// </summary>
    /// <param name="store">The persistent conversation store.</param>
    /// <param name="userId">The user the conversation belongs to (default "default").</param>
    /// <param name="threadId">Optional existing thread to resume; a new thread is created lazily when null.</param>
    /// <exception cref="ArgumentNullException">Thrown when store is null.</exception>
    public DbConversationMemory(IConversationStore store, string userId = "default", string? threadId = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrEmpty(userId);

        this.store = store;
        this.userId = userId;
        this.threadId = threadId;
    }

    /// <inheritdoc/>
    public string ConversationId => threadId ?? string.Empty;

    /// <inheritdoc/>
    public async Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var id = await EnsureThreadAsync(cancellationToken).ConfigureAwait(false);
        await store.AddMessageAsync(id, message, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var id = await EnsureThreadAsync(cancellationToken).ConfigureAwait(false);
        var stored = await store.GetMessagesAsync(id, cancellationToken).ConfigureAwait(false);
        return stored.Select(m => m.ToChatMessage()).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChatMessage>> GetTrimmedHistoryAsync(
        int maxTokens,
        Func<string, int> tokenCounter,
        CancellationToken cancellationToken = default)
    {
        var id = await EnsureThreadAsync(cancellationToken).ConfigureAwait(false);
        return await store.GetTrimmedHistoryAsync(id, maxTokens, tokenCounter, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (threadId is null) return;
        await store.DeleteThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        threadId = null;
    }

    /// <inheritdoc/>
    public async Task StartNewConversationAsync(
        string? conversationId = null,
        string? systemMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (conversationId is not null)
        {
            threadId = conversationId;
        }
        else
        {
            var thread = await store.CreateThreadAsync(userId, cancellationToken: cancellationToken).ConfigureAwait(false);
            threadId = thread.ThreadId;
        }

        if (!string.IsNullOrEmpty(systemMessage))
        {
            await store.AddMessageAsync(threadId, ChatMessage.System(systemMessage), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string> EnsureThreadAsync(CancellationToken cancellationToken)
    {
        if (threadId is not null) return threadId;

        await threadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (threadId is null)
            {
                var thread = await store.CreateThreadAsync(userId, cancellationToken: cancellationToken).ConfigureAwait(false);
                threadId = thread.ThreadId;
            }
            return threadId;
        }
        finally
        {
            threadLock.Release();
        }
    }
}
