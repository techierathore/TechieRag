namespace TechieRag.Models;

/// <summary>Per-call options for <see cref="Abstractions.IImageGenerationProvider.GenerateImageAsync"/> (REQ-RAG-155 / BRD-213).</summary>
public sealed class ImageGenerationOptions
{
    /// <summary>Gets or sets the model for this call in place of the provider's; null keeps the provider's.</summary>
    public string? Model { get; set; }

    /// <summary>Gets or sets what the image should not show, where the service supports it (Venice); null for none.</summary>
    public string? NegativePrompt { get; set; }

    /// <summary>Gets or sets a seed for a repeatable image, where the service supports it (Venice); null for random.</summary>
    public int? Seed { get; set; }

    /// <summary>Gets or sets the quality tier, <c>low</c>, <c>medium</c> or <c>high</c>, where the service supports it
    /// (OpenAI, Venice); null for the service default.</summary>
    public string? Quality { get; set; }

    /// <summary>Gets or sets the timeout for this call; null uses the provider's (120 seconds by default).</summary>
    public TimeSpan? Timeout { get; set; }
}
