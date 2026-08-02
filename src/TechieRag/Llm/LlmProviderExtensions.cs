using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// Capability queries over any <see cref="ILlmProvider"/> (REQ-RAG-039 / BRD-120).
/// </summary>
/// <remarks>
/// Extension methods rather than members on <see cref="ILlmProvider"/>, for the reason set out on
/// <see cref="IMultimodalLlmProvider"/>: the core interface is implemented by consumers of a published
/// package, so widening it breaks them. These methods answer the same question without that cost, and
/// they answer it for providers written before <see cref="IMultimodalLlmProvider"/> existed.
/// </remarks>
public static class LlmProviderExtensions
{
    /// <summary>Gets whether the provider can send images as chat input.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>True when the provider encodes image parts; false otherwise.</returns>
    public static bool SupportsVision(this ILlmProvider provider) =>
        provider.SupportsInput(ChatContentKind.Image);

    /// <summary>Gets whether the provider can send the given content kind as chat input.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="kind">The modality being asked about.</param>
    /// <returns>True when the provider encodes this kind; false when it would have to drop it.</returns>
    /// <remarks>
    /// A provider that does not implement <see cref="IMultimodalLlmProvider"/> is reported as text-only.
    /// That is the safe default and the honest one: it was written before the modality existed, so it
    /// cannot encode it.
    /// </remarks>
    public static bool SupportsInput(this ILlmProvider provider, ChatContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (kind == ChatContentKind.Text)
        {
            return true;
        }

        return provider is IMultimodalLlmProvider multimodal && multimodal.SupportsInput(kind);
    }
}
