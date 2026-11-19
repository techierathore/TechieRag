namespace TechieRag.Models;

/// <summary>
/// Options for <see cref="ITechieRag.AskWithEvidenceAsync"/>: what counts as a match, and how the answer is generated
/// (REQ-RAG-172, MyDiary TR-RAG-001).
/// </summary>
public sealed class EvidenceAskOptions
{
    /// <summary>Gets what counts as a match; see <see cref="MatchOptions"/>.</summary>
    public required MatchOptions Match { get; init; }

    /// <summary>Gets how many of the best matching chunks go into the prompt. Defaults to 8.</summary>
    public int MaxContextChunks { get; init; } = 8;

    /// <summary>Gets a system prompt in place of the configured one, or null to keep it.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Gets the model call's options, or null for the provider's defaults.</summary>
    public LlmCompletionOptions? CompletionOptions { get; init; }
}
