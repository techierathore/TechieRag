using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for persistent, thread-aware conversation storage.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Persists chat history across application restarts with first-class
/// conversation threads scoped to a user and optional workspace. Messages retain their
/// retrieval sources (citations) so past answers can be re-rendered with references.</para>
/// <para><b>Code Flow:</b> Configured via TechieRagBuilder.WithPersistence. The library owns
/// and self-creates its tables (TrThread, TrMessage) on <see cref="InitializeAsync"/>.</para>
/// <para><b>Implementations:</b> SqliteConversationStore, PostgresConversationStore.</para>
/// </remarks>
public interface IConversationStore
{
    /// <summary>
    /// Initializes the store, creating the TrThread and TrMessage tables if they do not exist.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous initialization.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new conversation thread for a user.
    /// </summary>
    /// <param name="userId">The owning user identifier.</param>
    /// <param name="workspaceId">Optional workspace the thread belongs to.</param>
    /// <param name="title">Optional thread title; defaults to "New Conversation".</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The created thread with its generated identifier.</returns>
    Task<ConversationThread> CreateThreadAsync(
        string userId,
        string? workspaceId = null,
        string? title = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a user's conversation threads ordered by most recently updated first.
    /// </summary>
    /// <param name="userId">The owning user identifier.</param>
    /// <param name="workspaceId">Optional workspace filter; null returns threads in all workspaces.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Threads ordered newest first.</returns>
    Task<IReadOnlyList<ConversationThread>> ListThreadsAsync(
        string userId,
        string? workspaceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single thread by identifier.
    /// </summary>
    /// <param name="threadId">The thread identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The thread, or null when it does not exist.</returns>
    Task<ConversationThread?> GetThreadAsync(string threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames an existing thread.
    /// </summary>
    /// <param name="threadId">The thread identifier.</param>
    /// <param name="title">The new title.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous rename.</returns>
    Task RenameThreadAsync(string threadId, string title, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a thread and all of its messages.
    /// </summary>
    /// <param name="threadId">The thread identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete.</returns>
    Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all conversation history (threads and messages) for a user.
    /// </summary>
    /// <param name="userId">The owning user identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete.</returns>
    Task DeleteAllForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a message to a thread, optionally persisting the retrieval sources
    /// (citations) that produced the answer.
    /// </summary>
    /// <param name="threadId">The thread identifier.</param>
    /// <param name="message">The chat message to persist.</param>
    /// <param name="sources">Optional search results cited by the message.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The stored message with its generated identifier.</returns>
    /// <param name="contentJson">
    /// The localizable form of the message text — a consumer-defined code plus arguments — or null
    /// when the text is not the consumer's own words (REQ-UI-059). Optional and defaulted, so no
    /// existing caller changes.
    /// </param>
    Task<StoredChatMessage> AddMessageAsync(
        string threadId,
        ChatMessage message,
        IReadOnlyList<SearchResult>? sources = null,
        CancellationToken cancellationToken = default,
        string? contentJson = null);

    /// <summary>
    /// Retrieves all messages in a thread in chronological order.
    /// </summary>
    /// <param name="threadId">The thread identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The thread's messages oldest first, including deserialized sources.</returns>
    Task<IReadOnlyList<StoredChatMessage>> GetMessagesAsync(
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a token-budgeted view of a thread's history for LLM context building.
    /// </summary>
    /// <param name="threadId">The thread identifier.</param>
    /// <param name="maxTokens">Maximum token budget for the returned history.</param>
    /// <param name="tokenCounter">Function that estimates the token count of a text.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The most recent messages that fit within the token budget, oldest first.</returns>
    /// <remarks>
    /// <para><b>Trimming:</b> Messages are kept from newest to oldest until the budget is
    /// exhausted, mirroring InMemoryConversationMemory trimming semantics.</para>
    /// </remarks>
    Task<IReadOnlyList<ChatMessage>> GetTrimmedHistoryAsync(
        string threadId,
        int maxTokens,
        Func<string, int> tokenCounter,
        CancellationToken cancellationToken = default);
}
