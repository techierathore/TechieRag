using TechieRag.Embedded;

namespace TechieRag.Local;

/// <summary>
/// Thrown before a local model loads when the device has less free memory than the model needs, so
/// the app keeps running instead of being killed by the operating system (REQ-RAG-060 / BRD-99).
/// </summary>
public sealed class LocalModelMemoryException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="modelId">The model that was not loaded.</param>
    /// <param name="requiredBytes">The free memory it needs.</param>
    /// <param name="availableBytes">The free memory the device reported.</param>
    public LocalModelMemoryException(string modelId, long requiredBytes, long availableBytes)
        : base($"The local model '{modelId}' needs about {ModelDownloadService.FormatBytes(requiredBytes)} of free memory, "
               + $"but only {ModelDownloadService.FormatBytes(availableBytes)} is available: "
               + $"{ModelDownloadService.FormatBytes(requiredBytes - availableBytes)} short. It was not loaded. "
               + "Close other apps, lower LocalLlmOptions.ContextSize, or choose a smaller model.")
    {
        ModelId = modelId;
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }

    /// <summary>Gets the model that was not loaded.</summary>
    public string ModelId { get; }

    /// <summary>Gets the free memory the model needs, in bytes.</summary>
    public long RequiredBytes { get; }

    /// <summary>Gets the free memory the device reported, in bytes.</summary>
    public long AvailableBytes { get; }

    /// <summary>Gets how many bytes were missing.</summary>
    public long ShortfallBytes => RequiredBytes - AvailableBytes;
}
