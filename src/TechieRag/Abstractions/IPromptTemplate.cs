using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for building prompts with RAG context injection.
/// </summary>
public interface IPromptTemplate
{
    /// <summary>Builds a system prompt with RAG context from search results.</summary>
    IReadOnlyList<ChatMessage> BuildRagPrompt(
        string userQuery,
        IReadOnlyList<SearchResult> searchResults,
        string? systemPrompt = null);

    /// <summary>Builds a chat prompt with RAG context and conversation history.</summary>
    IReadOnlyList<ChatMessage> BuildRagChatPrompt(
        string userMessage,
        IReadOnlyList<SearchResult> searchResults,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        string? systemPrompt = null);
}
