using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// What a built-in provider hands its stream reader: identity, token estimation for the fallback, and
/// the callback that raises the provider's completion telemetry (REQ-RAG-067).
/// </summary>
/// <param name="ProviderName">The provider's display name.</param>
/// <param name="ModelName">The model name reported on the completed event.</param>
/// <param name="Messages">The request messages, for the input-token estimate.</param>
/// <param name="EstimateTokens">The provider's token estimator.</param>
/// <param name="OnCompleted">Raises the provider's completion event with the final usage and whether tools were called.</param>
/// <param name="Route">The route the call was made on, if known.</param>
/// <param name="CallerTag">The caller tag from the completion options, if any.</param>
internal sealed record StreamReadContext(
    string ProviderName,
    string ModelName,
    IReadOnlyList<ChatMessage> Messages,
    Func<string, int> EstimateTokens,
    Action<TokenUsage, bool> OnCompleted,
    string? Route = null,
    string? CallerTag = null)
{
    /// <summary>Builds the usage record, estimating both sides when the service reported nothing.</summary>
    /// <param name="inputTokens">Reported input tokens.</param>
    /// <param name="outputTokens">Reported output tokens.</param>
    /// <param name="reasoningTokens">Reported reasoning tokens.</param>
    /// <param name="cacheReadTokens">Reported cached input tokens.</param>
    /// <param name="outputText">The streamed text, for the output estimate.</param>
    /// <param name="cacheWriteTokens">Reported cache-write tokens.</param>
    /// <returns>The usage for the completed event.</returns>
    public TokenUsage BuildUsage(int inputTokens, int outputTokens, int cacheReadTokens, string outputText, int cacheWriteTokens = 0, int reasoningTokens = 0)
    {
        // Same fallback the string streams have always used (TR-RAG-002): never report zero for a
        // call that plainly consumed tokens just because the server omitted its usage chunk.
        var isEstimated = inputTokens == 0 && outputTokens == 0;
        if (isEstimated)
        {
            inputTokens = Messages.Sum(m => EstimateTokens(m.Content ?? string.Empty));
            outputTokens = EstimateTokens(outputText);
            // Reasoning is never estimated: if the provider did not report it, it stays zero.
        }

        return new TokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ReasoningTokens = reasoningTokens,
            IsEstimated = isEstimated,
            Route = Route,
            CallerTag = CallerTag,
            CacheReadTokens = cacheReadTokens,
            CacheWriteTokens = cacheWriteTokens,
            ModelName = ModelName,
            ProviderName = ProviderName
        };
    }
}
