using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Default implementation of RAG prompt construction.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Builds well-structured prompts that combine the user's query
/// with relevant context from the vector store, formatted for optimal LLM performance.</para>
/// </remarks>
public class PromptTemplateEngine : IPromptTemplate
{
    private readonly PromptConfig config;

    /// <summary>
    /// Creates a new prompt template engine.
    /// </summary>
    /// <param name="config">Prompt configuration settings.</param>
    public PromptTemplateEngine(PromptConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        this.config = config;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ChatMessage> BuildRagPrompt(
        string userQuery,
        IReadOnlyList<SearchResult> searchResults,
        string? systemPrompt = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(userQuery);
        ArgumentNullException.ThrowIfNull(searchResults);

        var contextText = FormatContext(searchResults);
        var fullSystemPrompt = BuildSystemPromptWithContext(systemPrompt ?? config.SystemPrompt, contextText);

        return new List<ChatMessage>
        {
            ChatMessage.System(fullSystemPrompt),
            ChatMessage.User(userQuery)
        };
    }

    /// <inheritdoc/>
    public IReadOnlyList<ChatMessage> BuildRagChatPrompt(
        string userMessage,
        IReadOnlyList<SearchResult> searchResults,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        string? systemPrompt = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(userMessage);
        ArgumentNullException.ThrowIfNull(searchResults);

        var contextText = FormatContext(searchResults);
        var fullSystemPrompt = BuildSystemPromptWithContext(systemPrompt ?? config.SystemPrompt, contextText);

        var messages = new List<ChatMessage> { ChatMessage.System(fullSystemPrompt) };

        // Add conversation history (skip any existing system messages)
        if (conversationHistory is not null)
        {
            foreach (var msg in conversationHistory)
            {
                if (msg.Role != "system")
                {
                    messages.Add(msg);
                }
            }
        }

        messages.Add(ChatMessage.User(userMessage));
        return messages;
    }

    private string FormatContext(IReadOnlyList<SearchResult> searchResults)
    {
        if (searchResults.Count == 0)
            return string.Empty;

        var chunks = searchResults.Take(config.MaxContextChunks).ToList();
        var contextParts = new List<string>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var result = chunks[i];
            var source = result.Chunk.Metadata.TryGetValue("SourceFile", out var sf)
                ? sf?.ToString() ?? "unknown"
                : result.Chunk.Metadata.TryGetValue("FileName", out var fn)
                    ? fn?.ToString() ?? "unknown"
                    : "unknown";

            var formatted = config.ContextChunkTemplate
                .Replace("{index}", (i + 1).ToString())
                .Replace("{text}", result.Chunk.Text)
                .Replace("{source}", source)
                .Replace("{score:P0}", result.Score.ToString("P0"))
                .Replace("{score}", result.Score.ToString("F2"));

            contextParts.Add(formatted);
        }

        return string.Join("\n\n", contextParts);
    }

    private static string BuildSystemPromptWithContext(string systemPrompt, string contextText)
    {
        if (string.IsNullOrEmpty(contextText))
            return systemPrompt;

        return $"{systemPrompt}\n\n--- Retrieved Context ---\n{contextText}\n--- End Context ---";
    }
}
