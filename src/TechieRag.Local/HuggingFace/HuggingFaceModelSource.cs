using System.Text.RegularExpressions;
using TechieRag.Models;

namespace TechieRag.Local.HuggingFace;

/// <summary>
/// A model named on Hugging Face: repository, folder inside it and version, validated, and the paths in
/// the model root its files and its pin live at (REQ-RAG-108 / BRD-166).
/// </summary>
/// <remarks>
/// <para><b>Safe names only.</b> Every part becomes a URL segment and a folder name, so each is checked
/// against Hugging Face's own character set before anything else happens: letters, digits, <c>.</c>,
/// <c>_</c> and <c>-</c>, starting with a letter or digit, never <c>..</c> or <c>--</c> (Hugging Face
/// forbids both in repository names; refusing them here also keeps the folder names below unambiguous).
/// No part can climb out of the model root.</para>
/// <para><b>Folders.</b> The files of one commit live in
/// <c>&lt;root&gt;/hf--&lt;owner&gt;--&lt;name&gt;[--&lt;folder&gt;]--&lt;first 12 characters of the commit&gt;</c>,
/// lower case; the full commit is in the folder's manifest and is checked on reading. An unpinned name's
/// resolved commit is recorded in <c>&lt;root&gt;/hf--…[@&lt;version&gt;].pin</c>.</para>
/// </remarks>
internal sealed partial record HuggingFaceModelSource
{
    private HuggingFaceModelSource(string repository, string folder, string? version, string? modelRoot)
    {
        Repository = repository;
        Folder = folder;
        Version = version;
        ModelRootOverride = modelRoot;
    }

    /// <summary>Gets the repository id, <c>owner/name</c>.</summary>
    public string Repository { get; }

    /// <summary>Gets the folder inside the repository, without leading or trailing slashes; empty for its root.</summary>
    public string Folder { get; }

    /// <summary>Gets the version asked for (a commit, a branch or a tag), or null for the current one.</summary>
    public string? Version { get; }

    /// <summary>Gets the model root the files go to when not <see cref="ModelRoot.Current"/> (tests).</summary>
    public string? ModelRootOverride { get; }

    /// <summary>Gets the model's id: the repository, then the folder when there is one.</summary>
    public string Id => Folder.Length == 0 ? Repository : Repository + "/" + Folder;

    /// <summary>Gets whether the version is a full 40-character commit id, which needs no resolving.</summary>
    public bool IsExactCommit => Version is not null && CommitPattern().IsMatch(Version);

    /// <summary>Gets the model root in effect.</summary>
    public string Root => ModelRootOverride ?? ModelRoot.Current;

    /// <summary>Gets the file recording the commit an unpinned name (or a branch or tag) resolved to.</summary>
    public string PinPath => Path.Combine(Root, Stem + (Version is null ? string.Empty : "@" + Version.ToLowerInvariant()) + ".pin");

    private string Stem =>
        ("hf--" + Repository.Replace("/", "--", StringComparison.Ordinal)
            + (Folder.Length == 0 ? string.Empty : "--" + Folder.Replace("/", "--", StringComparison.Ordinal)))
        .ToLowerInvariant();

    /// <summary>
    /// Validates a Hugging Face name.
    /// </summary>
    /// <param name="repository">The repository id, <c>owner/name</c>.</param>
    /// <param name="folder">The folder inside it, or null for its root.</param>
    /// <param name="version">A commit, branch or tag, or null for the current version.</param>
    /// <param name="modelRoot">A model root other than <see cref="ModelRoot.Current"/>, or null.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentException">A part is empty, malformed or could escape the model root.</exception>
    public static HuggingFaceModelSource Create(string repository, string? folder, string? version, string? modelRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        var parts = repository.Split('/');
        if (parts.Length != 2 || !parts.All(IsSafeSegment))
        {
            throw new ArgumentException(
                $"'{repository}' is not a Hugging Face repository id. Use 'owner/name', each part letters, digits, '.', '_' or '-'.",
                nameof(repository));
        }

        var trimmedFolder = (folder ?? string.Empty).Trim('/');
        if (trimmedFolder.Length > 0 && !trimmedFolder.Split('/').All(IsSafeSegment))
        {
            throw new ArgumentException(
                $"'{folder}' is not a folder name inside a Hugging Face repository: use segments of letters, digits, '.', '_' or '-' separated by '/'.",
                nameof(folder));
        }

        if (version is not null && !IsSafeSegment(version))
        {
            throw new ArgumentException(
                $"'{version}' is not a Hugging Face version: use a commit id, or a branch or tag of letters, digits, '.', '_' or '-'.",
                nameof(version));
        }

        return new HuggingFaceModelSource(repository, trimmedFolder, version, modelRoot);
    }

    /// <summary>Gets the folder one commit's files live in.</summary>
    /// <param name="commit">The 40-character commit id.</param>
    /// <returns>An absolute folder path; it is not created.</returns>
    public string GetDirectory(string commit) => Path.Combine(Root, FolderName(commit));

    /// <summary>Gets the folder name one commit's files live in, under the model root.</summary>
    /// <param name="commit">The 40-character commit id.</param>
    /// <returns>The folder name.</returns>
    public string FolderName(string commit) => Stem + "--" + commit[..12].ToLowerInvariant();

    /// <summary>Gets whether a text is a full 40-character lower-case hex commit id.</summary>
    /// <param name="value">The text.</param>
    /// <returns>True for a commit id.</returns>
    public static bool IsCommit(string? value) => value is not null && CommitPattern().IsMatch(value);

    private static bool IsSafeSegment(string segment) =>
        SegmentPattern().IsMatch(segment)
        && !segment.Contains("..", StringComparison.Ordinal)
        && !segment.Contains("--", StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,95}$")]
    private static partial Regex SegmentPattern();

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitPattern();
}
