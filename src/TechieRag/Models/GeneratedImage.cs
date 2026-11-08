namespace TechieRag.Models;

/// <summary>An image a provider generated (REQ-RAG-155 / BRD-213).</summary>
/// <param name="Bytes">The encoded image.</param>
/// <param name="MediaType">The media type of <paramref name="Bytes"/>, read from the bytes themselves, for example <c>image/png</c>.</param>
/// <param name="Size">The pixel size read from the image header; the asked size when the header cannot be read.</param>
/// <param name="Usage">What the call used.</param>
public sealed record GeneratedImage(byte[] Bytes, string MediaType, ImageSize Size, ImageGenerationUsage Usage);
