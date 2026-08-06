using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Optional capability interface for providers that accept non-text chat input (REQ-RAG-039 / BRD-120).
/// </summary>
/// <remarks>
/// <para><b>Why this is a separate interface.</b> TechieRag ships as a NuGet package and
/// <see cref="ILlmProvider"/> is implemented by consumers — a custom provider in a consumer's own
/// assembly is a supported and expected thing to write. Adding <c>SupportsVision</c> to
/// <see cref="ILlmProvider"/> would break every one of those on upgrade, for a capability most of them
/// do not have. A separate interface costs implementers nothing: a provider that ignores it is simply
/// reported as text-only, which is the truthful answer.</para>
/// <para><b>Why a method and not properties.</b> <c>SupportsInput(kind)</c> rather than
/// <c>SupportsVision</c>/<c>SupportsAudio</c>/<c>SupportsDocuments</c> means the audio and document
/// modalities that follow vision (BRD-120) arrive as new <see cref="ChatContentKind"/> members and
/// break nobody a second time.</para>
/// <para><b>What "supports" means here.</b> The provider can encode this modality in its API's wire
/// format. It cannot mean the configured model will accept it — the same Anthropic endpoint serves
/// vision and non-vision models, and no provider exposes that per-model fact without a network call.
/// Choosing a model that can see is the operator's decision; this interface only says the library will
/// not silently drop the image on the way out.</para>
/// </remarks>
public interface IMultimodalLlmProvider
{
    /// <summary>Gets whether this provider can send the given content kind to its API.</summary>
    /// <param name="kind">The modality being asked about.</param>
    /// <returns>True when the provider encodes this kind; false when it would have to drop it.</returns>
    bool SupportsInput(ChatContentKind kind);
}
