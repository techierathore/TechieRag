namespace TechieDesk.Services.Storage;

/// <summary>
/// Declares an artefact the data directory is expected to hold, before it is measured
/// (REQ-UI-041).
/// </summary>
/// <param name="NameKey">Resource key for the name shown in the data/storage table.</param>
/// <param name="DescriptionKey">Resource key for the line saying what the artefact holds.</param>
/// <param name="RelativePath">Path relative to the data directory; a file name or a sub-directory.</param>
/// <remarks>
/// REQ-UI-051: the two display members are resource KEYS, not English. The path stays invariant —
/// it is a real name on disk and translating it would name a file that does not exist.
/// </remarks>
public sealed record DataStorageArtefactDefinition(string NameKey, string DescriptionKey, string RelativePath);
