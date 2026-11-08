namespace TechieRag.Models;

/// <summary>What one image generation call used (REQ-RAG-155 / BRD-213).</summary>
/// <remarks>Token counts are null where the service reports none (Venice reports no usage in its response; its
/// prices are per image, listed by its models endpoint).</remarks>
public sealed record ImageGenerationUsage
{
    /// <summary>Gets the connector that served the call, for example <c>openai</c>.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Gets the model that made the image.</summary>
    public required string ModelName { get; init; }

    /// <summary>Gets the number of images returned.</summary>
    public int Images { get; init; } = 1;

    /// <summary>Gets the input tokens the service counted, or null when it reports none.</summary>
    public int? InputTokens { get; init; }

    /// <summary>Gets the output tokens the service counted, or null when it reports none.</summary>
    public int? OutputTokens { get; init; }

    /// <summary>Gets the total tokens the service counted, or null when it reports none.</summary>
    public int? TotalTokens { get; init; }

    /// <summary>Gets how long the call took.</summary>
    public TimeSpan Duration { get; init; }
}
