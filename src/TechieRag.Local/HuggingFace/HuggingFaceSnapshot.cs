using System.Text.Json;
using System.Text.Json.Nodes;
using TechieRag.Local.Runtime;

namespace TechieRag.Local.HuggingFace;

/// <summary>
/// One Hugging Face model at one commit: its licence, where its terms are, and the runtime files with
/// the fingerprints Hugging Face publishes for them (REQ-RAG-108 / BRD-166).
/// </summary>
/// <remarks>
/// Written as <see cref="ManifestFileName"/> into the model's folder as soon as it is resolved, so a later
/// start — offline — knows the licence, the file list and every fingerprint without asking Hugging Face.
/// </remarks>
/// <param name="Repository">The repository id.</param>
/// <param name="Folder">The folder inside it; empty for its root.</param>
/// <param name="Commit">The 40-character commit id the files are pinned to.</param>
/// <param name="LicenceName">The model card's licence, for example <c>gemma</c> or <c>apache-2.0</c>.</param>
/// <param name="TermsUrl">Where the licence text is.</param>
/// <param name="BaseUrl">The address the files are fetched under: <c>&lt;hub&gt;/&lt;repository&gt;/resolve/&lt;commit&gt;</c>.</param>
/// <param name="Files">The runtime files, each with its size and fingerprint.</param>
internal sealed record HuggingFaceSnapshot(
    string Repository,
    string Folder,
    string Commit,
    string LicenceName,
    Uri TermsUrl,
    string BaseUrl,
    IReadOnlyList<LocalModelFileSpec> Files)
{
    /// <summary>The manifest's file name inside the model folder.</summary>
    internal const string ManifestFileName = ".techierag-huggingface.json";

    /// <summary>Gets the total size of the files in bytes.</summary>
    public long DownloadBytes => Files.Sum(f => f.Bytes);

    /// <summary>
    /// Builds the file set a download fetches and a check verifies.
    /// </summary>
    /// <param name="folderName">The folder under the model root.</param>
    /// <returns>The variant.</returns>
    public LocalModelVariant ToVariant(string folderName) => new(LocalModelFormat.OnnxGenAi, folderName, BaseUrl, Files);

    /// <summary>Writes the manifest into a folder, creating the folder.</summary>
    /// <param name="directory">The model folder.</param>
    public void WriteManifest(string directory)
    {
        Directory.CreateDirectory(directory);
        var files = new JsonArray();
        foreach (var file in Files)
        {
            files.Add(new JsonObject
            {
                ["name"] = file.FileName,
                ["path"] = file.RemotePath,
                ["bytes"] = file.Bytes,
                ["hash"] = file.Hash,
                ["hashKind"] = file.HashKind.ToString()
            });
        }

        var manifest = new JsonObject
        {
            ["repository"] = Repository,
            ["folder"] = Folder,
            ["commit"] = Commit,
            ["licence"] = LicenceName,
            ["termsUrl"] = TermsUrl.ToString(),
            ["baseUrl"] = BaseUrl,
            ["files"] = files
        };
        var path = Path.Combine(directory, ManifestFileName);
        File.WriteAllText(path + ".tmp", manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    /// <summary>
    /// Reads a folder's manifest when it describes exactly this repository, folder and commit.
    /// </summary>
    /// <param name="directory">The model folder.</param>
    /// <param name="source">The name the manifest must match.</param>
    /// <param name="commit">The commit it must match.</param>
    /// <returns>The snapshot, or null when there is no matching, readable manifest.</returns>
    public static HuggingFaceSnapshot? TryReadManifest(string directory, HuggingFaceModelSource source, string commit)
    {
        var path = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidOperationException("The manifest is not a JSON object.");
            var snapshot = new HuggingFaceSnapshot(
                Text(root, "repository"),
                Text(root, "folder"),
                Text(root, "commit"),
                Text(root, "licence"),
                new Uri(Text(root, "termsUrl")),
                Text(root, "baseUrl"),
                (root["files"] as JsonArray ?? throw new InvalidOperationException("No files.")).Select(ReadFile).ToList());
            return string.Equals(snapshot.Repository, source.Repository, StringComparison.OrdinalIgnoreCase)
                && string.Equals(snapshot.Folder, source.Folder, StringComparison.OrdinalIgnoreCase)
                && snapshot.Commit == commit
                    ? snapshot
                    : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or UriFormatException
                                              or ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static LocalModelFileSpec ReadFile(JsonNode? node)
    {
        var name = Text(node, "name");
        if (!HuggingFaceFileSelector.IsSafeFileName(name))
        {
            throw new InvalidOperationException($"The manifest names an unsafe file '{name}'.");
        }

        return new LocalModelFileSpec(
            name,
            Text(node, "path"),
            Number(node, "bytes"),
            Text(node, "hash"),
            Enum.Parse<LocalModelHashKind>(Text(node, "hashKind")));
    }

    private static string Text(JsonNode? node, string name) =>
        node?[name]?.GetValue<string>() ?? throw new InvalidOperationException($"The manifest has no '{name}'.");

    private static long Number(JsonNode? node, string name) =>
        node?[name]?.GetValue<long>() ?? throw new InvalidOperationException($"The manifest has no '{name}'.");
}
