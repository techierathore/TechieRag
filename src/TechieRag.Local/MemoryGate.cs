namespace TechieRag.Local;

/// <summary>
/// Refuses to load a model the device has no room for, before any memory is taken, so the app
/// keeps running instead of being killed by the operating system (REQ-RAG-060 / BRD-99).
/// </summary>
internal sealed class MemoryGate
{
    private readonly Func<long?> readAvailableBytes;

    /// <summary>Creates a gate over a memory reading.</summary>
    /// <param name="readAvailableBytes">Returns the free memory in bytes, or null when unknown.</param>
    public MemoryGate(Func<long?> readAvailableBytes)
    {
        ArgumentNullException.ThrowIfNull(readAvailableBytes);
        this.readAvailableBytes = readAvailableBytes;
    }

    /// <summary>Gets the gate over this platform's real memory reading.</summary>
    public static MemoryGate ForThisDevice { get; } = new(AvailableMemory.Read);

    /// <summary>
    /// Checks that a load fits.
    /// </summary>
    /// <param name="modelId">The model, named in the message.</param>
    /// <param name="requiredBytes">The free memory the load needs.</param>
    /// <returns>The free memory read, or null when the platform gave no reading and the load proceeds.</returns>
    /// <exception cref="LocalModelMemoryException">Less memory is free than the load needs.</exception>
    public long? Ensure(string modelId, long requiredBytes)
    {
        var available = readAvailableBytes();
        if (available is { } free && free < requiredBytes)
        {
            throw new LocalModelMemoryException(modelId, requiredBytes, free);
        }

        return available;
    }
}
