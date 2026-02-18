using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Optional abstraction for managing conversation history.
/// </summary>
public interface IConversationMemory
{
    /// <summary>Gets the current conversation ID.</summary>
    string ConversationId { get; }

    /// <summary>Adds a message to the conversation history.</summary>
    Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);

    /// <summary>Retrieves all messages in the current conversation.</summary>
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Trims conversation history to fit within a token limit.</summary>
    Task<IReadOnlyList<ChatMessage>> GetTrimmedHistoryAsync(
        int maxTokens,
        Func<string, int> tokenCounter,
        CancellationToken cancellationToken = default);

    /// <summary>Clears all messages from the current conversation.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts a new conversation, optionally preserving the system message.</summary>
    Task StartNewConversationAsync(
        string? conversationId = null,
        string? systemMessage = null,
        CancellationToken cancellationToken = default);
}
