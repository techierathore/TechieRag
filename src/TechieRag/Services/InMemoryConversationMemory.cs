using System.Collections.Concurrent;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// In-memory implementation of conversation history management.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides simple, thread-safe conversation memory for multi-turn
/// chat. Supports automatic context window management by trimming oldest messages.</para>
/// <para><b>Limitations:</b> History is lost when the application restarts.
/// For persistent memory, implement a database-backed IConversationMemory.</para>
/// </remarks>
public class InMemoryConversationMemory : IConversationMemory
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> conversations = new();
    private readonly object syncLock = new();
    private string currentConversationId;

    /// <inheritdoc/>
    public string ConversationId => currentConversationId;

    /// <summary>
    /// Creates a new in-memory conversation memory instance.
    /// </summary>
    /// <param name="conversationId">Optional initial conversation ID.</param>
    public InMemoryConversationMemory(string? conversationId = null)
    {
        currentConversationId = conversationId ?? Guid.NewGuid().ToString();
        conversations.TryAdd(currentConversationId, new List<ChatMessage>());
    }

    /// <inheritdoc/>
    public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messages = conversations.GetOrAdd(currentConversationId, _ => new List<ChatMessage>());
        lock (syncLock)
        {
            messages.Add(message);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var messages = conversations.GetOrAdd(currentConversationId, _ => new List<ChatMessage>());
        lock (syncLock)
        {
            return Task.FromResult<IReadOnlyList<ChatMessage>>(messages.ToList());
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ChatMessage>> GetTrimmedHistoryAsync(
        int maxTokens,
        Func<string, int> tokenCounter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);

        var messages = conversations.GetOrAdd(currentConversationId, _ => new List<ChatMessage>());

        lock (syncLock)
        {
            if (messages.Count == 0)
                return Task.FromResult<IReadOnlyList<ChatMessage>>(Array.Empty<ChatMessage>());

            var result = new List<ChatMessage>();
            ChatMessage? systemMessage = null;
            int totalTokens = 0;

            // Always keep the system message if present
            if (messages.Count > 0 && messages[0].Role == "system")
            {
                systemMessage = messages[0];
                totalTokens += tokenCounter(systemMessage.Content ?? string.Empty);
            }

            // Add messages from newest to oldest (excluding system)
            var nonSystemMessages = systemMessage != null ? messages.Skip(1).ToList() : messages.ToList();

            for (int i = nonSystemMessages.Count - 1; i >= 0; i--)
            {
                var msg = nonSystemMessages[i];
                var msgTokens = tokenCounter(msg.Content ?? string.Empty);

                if (totalTokens + msgTokens > maxTokens)
                    break;

                totalTokens += msgTokens;
                result.Insert(0, msg);
            }

            // Prepend system message
            if (systemMessage != null)
            {
                result.Insert(0, systemMessage);
            }

            return Task.FromResult<IReadOnlyList<ChatMessage>>(result);
        }
    }

    /// <inheritdoc/>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var messages = conversations.GetOrAdd(currentConversationId, _ => new List<ChatMessage>());
        lock (syncLock)
        {
            messages.Clear();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartNewConversationAsync(
        string? conversationId = null,
        string? systemMessage = null,
        CancellationToken cancellationToken = default)
    {
        currentConversationId = conversationId ?? Guid.NewGuid().ToString();
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(systemMessage))
        {
            messages.Add(ChatMessage.System(systemMessage));
        }

        conversations[currentConversationId] = messages;
        return Task.CompletedTask;
    }
}
