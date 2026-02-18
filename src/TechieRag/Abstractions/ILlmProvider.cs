using System.Runtime.CompilerServices;
using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for Large Language Model (LLM) interaction services.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a unified interface for text generation, chat completions,
/// streaming responses, and tool calling across different LLM providers.</para>
/// <para><b>Implementations:</b> OllamaLlmProvider, LmStudioLlmProvider,
/// OpenAICompatibleLlmProvider, AzureAIFoundryLlmProvider, GoogleGeminiLlmProvider,
/// AnthropicLlmProvider</para>
/// </remarks>
public interface ILlmProvider
{
    /// <summary>Gets the display name of this LLM provider.</summary>
    string Name { get; }

    /// <summary>Gets the name of the LLM model being used.</summary>
    string ModelName { get; }

    /// <summary>Gets whether this provider supports tool/function calling.</summary>
    bool SupportsToolCalling { get; }

    /// <summary>Gets whether this provider supports streaming responses.</summary>
    bool SupportsStreaming { get; }

    /// <summary>Generates a text completion for a single prompt.</summary>
    Task<LlmResponse> CompleteAsync(
        string prompt,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Generates a streaming text completion for a single prompt.</summary>
    IAsyncEnumerable<string> CompleteStreamAsync(
        string prompt,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a multi-turn chat conversation and returns the assistant response.</summary>
    Task<LlmResponse> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a multi-turn chat conversation and streams the response.</summary>
    IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Generates a typed/structured response by requesting JSON output from the LLM.</summary>
    Task<T> CompleteAsync<T>(
        string prompt,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>Estimates the token count for a given text.</summary>
    int EstimateTokenCount(string text);

    /// <summary>Event raised after each LLM completion, providing token usage metrics.</summary>
    event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;
}

/// <summary>Event arguments for LLM completion telemetry.</summary>
public class LlmCompletionEventArgs : EventArgs
{
    /// <summary>Gets the number of input/prompt tokens.</summary>
    public required int InputTokens { get; init; }

    /// <summary>Gets the number of output/completion tokens.</summary>
    public required int OutputTokens { get; init; }

    /// <summary>Gets the total tokens (input + output).</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>Gets the duration of the LLM operation.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Gets the model name used.</summary>
    public required string ModelName { get; init; }

    /// <summary>Gets the provider name used.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Gets whether this was a streaming request.</summary>
    public bool IsStreaming { get; init; }

    /// <summary>Gets whether tool calls were involved.</summary>
    public bool InvolvedToolCalls { get; init; }
}
