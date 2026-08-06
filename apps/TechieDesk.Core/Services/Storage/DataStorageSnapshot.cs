namespace TechieDesk.Services.Storage;

/// <summary>
/// A point-in-time reading of the TechieDesk data directory: where it is, what is in it and how
/// much of the volume it occupies (REQ-UI-041, BRD-133).
/// </summary>
/// <param name="DirectoryPath">Absolute path of the data directory (REQ-FN-037).</param>
/// <param name="DirectoryExists">True when the directory is present on disk.</param>
/// <param name="Artefacts">Every artefact measured, including empty ones and the remainder row.</param>
/// <param name="TotalSizeBytes">Total bytes the directory occupies, at any depth.</param>
/// <param name="VolumeFreeBytes">Free bytes on the volume holding the directory; zero when unknown.</param>
/// <param name="VolumeTotalBytes">Size of that volume in bytes; zero when unknown.</param>
public sealed record DataStorageSnapshot(
    string DirectoryPath,
    bool DirectoryExists,
    IReadOnlyList<DataStorageArtefact> Artefacts,
    long TotalSizeBytes,
    long VolumeFreeBytes,
    long VolumeTotalBytes)
{
    /// <summary>Gets the number of artefacts that actually exist on disk.</summary>
    public int PresentArtefactCount => Artefacts.Count(artefact => artefact.Exists);

    /// <summary>
    /// Gets the share of the volume the data directory occupies, as a percentage from 0 to 100.
    /// </summary>
    /// <remarks>
    /// Returns zero when the volume size is unknown, so the progress bar reads empty rather than
    /// full. A divide that silently produced 100% would claim the disk was full.
    /// </remarks>
    public double VolumeUsedPercent => VolumeTotalBytes <= 0
        ? 0
        : Math.Clamp(TotalSizeBytes * 100d / VolumeTotalBytes, 0d, 100d);
}
