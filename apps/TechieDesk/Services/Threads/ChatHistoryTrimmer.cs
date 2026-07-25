using TechieRag.Models;

namespace TechieDesk.Services.Threads;

/// <summary>
/// Prepares persisted conversation history for the workspace-scoped streaming RAG call.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The workspace chat persists the user's question before answering
/// (REQ-RAG-008), so the trimmed history it reads back already ends with that question. The
/// library's RAG chat template appends the question itself, so the trailing user turn must be
/// dropped or the LLM sees it twice (REQ-RAG-009 / REQ-RAG-013).</para>
/// </remarks>
public static class ChatHistoryTrimmer
{
    /// <summary>
    /// Returns the prior conversation turns, dropping a trailing user turn that repeats the
    /// question about to be asked.
    /// </summary>
    /// <param name="history">The token-trimmed history read from the conversation store.</param>
    /// <returns>The history without its trailing user turn, or the history unchanged when it
    /// does not end with one.</returns>
    public static IReadOnlyList<ChatMessage> PriorTurns(IReadOnlyList<ChatMessage> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        return history.Count > 0 && history[^1].Role == "user"
            ? history.Take(history.Count - 1).ToList()
            : history.ToList();
    }
}
