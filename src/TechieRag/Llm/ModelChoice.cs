using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>How a <see cref="ModelChoice"/> was reached.</summary>
public enum ModelChoiceSource
{
    /// <summary>The small model named one of the candidates.</summary>
    SmallModel,

    /// <summary>Only one candidate was offered, so no model was asked.</summary>
    OnlyCandidate,

    /// <summary>The small model's reply named no candidate, so the fallback candidate was used.</summary>
    Fallback
}

/// <summary>
/// The model a <see cref="ModelChooser"/> chose, and why (REQ-RAG-128, Chatur TR-RAG-006).
/// </summary>
/// <param name="Candidate">The chosen candidate, as the host passed it.</param>
/// <param name="Reason">One sentence a host can show and log.</param>
/// <param name="Source">Whether the small model chose, or no choice was needed, or the fallback was used.</param>
/// <param name="Usage">The small model's token usage; null when no model was asked.</param>
public sealed record ModelChoice(
    ModelCandidate Candidate,
    string Reason,
    ModelChoiceSource Source,
    TokenUsage? Usage)
{
    /// <summary>Gets the chosen model's id.</summary>
    public string ModelId => Candidate.ModelId;
}
