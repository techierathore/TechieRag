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
internal sealed record StreamReadContext(
    string ProviderName,
    string ModelName,
    IReadOnlyList<ChatMessage> Messages,
    Func<string, int> EstimateTokens,
    Action<TokenUsage, bool> OnCompleted)
{
    /// <summary>Builds the usage record, estimating both sides when the service reported nothing.</summary>
    /// <param name="inputTokens">Reported input tokens.</param>
    /// <param name="outputTokens">Reported output tokens.</param>
    /// <param name="cacheReadTokens">Reported cached input tokens.</param>
    /// <param name="outputText">The streamed text, for the output estimate.</param>
    /// <param name="cacheWriteTokens">Reported cache-write tokens.</param>
    /// <returns>The usage for the completed event.</returns>
    public TokenUsage BuildUsage(int inputTokens, int outputTokens, int cacheReadTokens, string outputText, int cacheWriteTokens = 0)
    {
        // Same fallback the string streams have always used (TR-RAG-002): never report zero for a
        // call that plainly consumed tokens just because the server omitted its usage chunk.
        if (inputTokens == 0 && outputTokens == 0)
        {
            inputTokens = Messages.Sum(m => EstimateTokens(m.Content ?? string.Empty));
            outputTokens = EstimateTokens(outputText);
        }

        return new TokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheWriteTokens = cacheWriteTokens,
            ModelName = ModelName,
            ProviderName = ProviderName
        };
    }
}
