namespace TechieRag.Local.Runtime;

/// <summary>
/// Everything a runtime needs to generate one answer, already resolved from the call's options and
/// the provider's defaults (REQ-RAG-059 / BRD-98).
/// </summary>
internal sealed record LocalGenerationSettings
{
    /// <summary>Gets the most tokens to generate; already clamped to the room left in the context.</summary>
    public required int MaxTokens { get; init; }

    /// <summary>Gets the sampling temperature; 0 means greedy.</summary>
    public required float Temperature { get; init; }

    /// <summary>Gets the nucleus sampling value.</summary>
    public required float TopP { get; init; }

    /// <summary>Gets the seed for reproducible sampling, or null.</summary>
    public int? Seed { get; init; }

    /// <summary>Gets the frequency penalty, or null.</summary>
    public float? FrequencyPenalty { get; init; }

    /// <summary>Gets the presence penalty, or null.</summary>
    public float? PresencePenalty { get; init; }

    /// <summary>
    /// Gets the stop sequences: the caller's plus the template's end-of-turn marker. The provider cuts
    /// the text at them whatever the runtime does; a runtime may also stop early on them.
    /// </summary>
    public IReadOnlyList<string> StopSequences { get; init; } = [];

    /// <summary>Gets whether the answer must be JSON.</summary>
    public bool JsonMode { get; init; }

    /// <summary>Gets the JSON schema the answer must follow, or null; a runtime constrains to it where it can.</summary>
    public string? JsonSchema { get; init; }
}
