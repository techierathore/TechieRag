using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Default implementation of RAG prompt construction.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Builds well-structured prompts that combine the user's query
/// with relevant context from the vector store, formatted for optimal LLM performance.</para>
/// <para><b>Truncation is signalled, never silent (REQ-RAG-096 / BRD-143, TR-RAG-009).</b> When
/// <see cref="PromptConfig.MaxContextChunks"/> drops search results, the engine logs a warning and
/// raises <see cref="ContextTruncated"/> with the same <see cref="ContextTruncatedEventArgs"/> and
/// <see cref="WorkspaceContext"/> diagnostics <see cref="WorkspaceManager.ContextTruncated"/> uses,
/// so one handler serves both. A budget of zero or less disables trimming, as it does there.</para>
/// </remarks>
public class PromptTemplateEngine : IPromptTemplate
{
    private readonly PromptConfig config;
    private readonly ILogger<PromptTemplateEngine> logger;

    /// <summary>
    /// Creates a new prompt template engine.
    /// </summary>
    /// <param name="config">Prompt configuration settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    public PromptTemplateEngine(PromptConfig config)
        : this(config, null)
    {
    }

    /// <summary>
    /// Creates a new prompt template engine that logs context truncation.
    /// </summary>
    /// <param name="config">Prompt configuration settings.</param>
    /// <param name="logger">Receives a warning whenever context is truncated; null logs nothing.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    public PromptTemplateEngine(PromptConfig config, ILogger<PromptTemplateEngine>? logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        this.config = config;
        this.logger = logger ?? NullLogger<PromptTemplateEngine>.Instance;
    }

    /// <summary>
    /// Raised when <see cref="PromptConfig.MaxContextChunks"/> dropped search results while a prompt
    /// was built (REQ-RAG-096). <see cref="ContextTruncatedEventArgs.WorkspaceId"/> is empty because
    /// the engine does not know which workspace, if any, the results came from;
    /// <see cref="ContextTruncatedEventArgs.Context"/> counts every dropped result as retrieved.
    /// </summary>
    public event EventHandler<ContextTruncatedEventArgs>? ContextTruncated;

    /// <inheritdoc/>
    public IReadOnlyList<ChatMessage> BuildRagPrompt(
        string userQuery,
        IReadOnlyList<SearchResult> searchResults,
        string? systemPrompt = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(userQuery);
        ArgumentNullException.ThrowIfNull(searchResults);

        var contextText = FormatContext(userQuery, searchResults);
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

        var contextText = FormatContext(userMessage, searchResults);
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

    private string FormatContext(string question, IReadOnlyList<SearchResult> searchResults)
    {
        if (searchResults.Count == 0)
            return string.Empty;

        var chunks = ApplyContextBudget(question, searchResults);
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

    /// <summary>
    /// Trims the results to <see cref="PromptConfig.MaxContextChunks"/> and signals any truncation.
    /// </summary>
    /// <param name="question">The user text the prompt is being built for.</param>
    /// <param name="searchResults">The results offered as context, most relevant first.</param>
    /// <returns>The results that fit the budget.</returns>
    private List<SearchResult> ApplyContextBudget(string question, IReadOnlyList<SearchResult> searchResults)
    {
        var limit = config.MaxContextChunks;
        if (limit <= 0 || searchResults.Count <= limit)
            return searchResults.ToList();

        var kept = searchResults.Take(limit).ToList();
        var context = new WorkspaceContext
        {
            Results = kept,
            PinnedIncluded = 0,
            RetrievedIncluded = kept.Count,
            PinnedEvicted = 0,
            RetrievedEvicted = searchResults.Count - kept.Count,
            MaxContextChunks = limit
        };

        logger.LogWarning(
            "Prompt context truncated to MaxContextChunks={Limit}: dropped {Dropped} of {Offered} search result(s).",
            limit, context.RetrievedEvicted, searchResults.Count);

        ContextTruncated?.Invoke(this, new ContextTruncatedEventArgs
        {
            WorkspaceId = string.Empty,
            Question = question,
            Context = context
        });

        return kept;
    }

    private static string BuildSystemPromptWithContext(string systemPrompt, string contextText)
    {
        if (string.IsNullOrEmpty(contextText))
            return systemPrompt;

        return $"{systemPrompt}\n\n--- Retrieved Context ---\n{contextText}\n--- End Context ---";
    }
}
