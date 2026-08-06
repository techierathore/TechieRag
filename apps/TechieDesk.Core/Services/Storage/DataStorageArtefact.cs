namespace TechieDesk.Services.Storage;

/// <summary>
/// One artefact TechieDesk keeps inside its data directory, with the space it occupies
/// (REQ-UI-041, BRD-133).
/// </summary>
/// <param name="NameKey">Resource key for the name shown in the data/storage table.</param>
/// <param name="DescriptionKey">Resource key for the line saying what the artefact holds.</param>
/// <param name="RelativePath">Path relative to the data directory, e.g. <c>techiedesk.db</c>.</param>
/// <param name="FullPath">Absolute path on disk. Present whether or not the artefact exists.</param>
/// <param name="SizeBytes">Bytes occupied; zero when the artefact does not exist yet.</param>
/// <param name="LastWrittenUtc">Last write time in UTC, or null when the artefact does not exist.</param>
/// <param name="Exists">True when a file or directory is present at <paramref name="FullPath"/>.</param>
/// <remarks>
/// A missing artefact is reported with <c>Exists = false</c> and a zero size rather than being
/// dropped from the list. A fresh install has no <c>uploads/</c> directory and no downloaded model,
/// and a table that silently omits them reads as "TechieDesk does not have these" instead of "these
/// are empty" — the row is the honest answer to "where did my disk go".
/// </para>
/// <para>
/// REQ-UI-051: <paramref name="NameKey"/> and <paramref name="DescriptionKey"/> are resource keys
/// resolved by whichever surface renders the row, so this type cannot carry English to a screen.
/// </remarks>
public sealed record DataStorageArtefact(
    string NameKey,
    string DescriptionKey,
    string RelativePath,
    string FullPath,
    long SizeBytes,
    DateTimeOffset? LastWrittenUtc,
    bool Exists);
