namespace TechieRag.Llm;

/// <summary>
/// One model a <see cref="ModelChooser"/> may choose (REQ-RAG-128, Chatur TR-RAG-006).
/// </summary>
/// <remarks>
/// Everything but <see cref="ModelId"/> is free text shown to the small model as written, so a host
/// describes its models in its own words: <c>Tier = "fast"</c>, <c>Notes = "good at code"</c>,
/// <c>Cost = "free, runs locally"</c>.
/// </remarks>
public sealed record ModelCandidate
{
    /// <summary>Gets the model id the host routes by, such as <c>openai/gpt-5-mini</c>; returned as chosen.</summary>
    public required string ModelId { get; init; }

    /// <summary>Gets the host's tier for this model, such as <c>fast</c> or <c>deep</c>; null says nothing.</summary>
    public string? Tier { get; init; }

    /// <summary>Gets what this model is good or bad at; null says nothing.</summary>
    public string? Notes { get; init; }

    /// <summary>Gets what this model costs, in the host's words; null says nothing.</summary>
    public string? Cost { get; init; }
}
