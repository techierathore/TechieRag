using TechieRag.Embedded;

namespace TechieRag.Local.Runtime;

/// <summary>
/// One model in one runtime's format: its folder under the model root, its files and the memory it
/// needs per token of context.
/// </summary>
/// <param name="Format">The format a runtime must load.</param>
/// <param name="FolderName">The folder under the model root, for example <c>qwen2.5-0.5b-instruct-onnx</c>.</param>
/// <param name="DefaultBaseUrl">
/// The base URL files are fetched from, pinned to one repository commit; null for a file set that is
/// served only from a mirror (<c>TECHIERAG_MODEL_BASE_URL</c>) until its default address is known.
/// </param>
/// <param name="Files">The files, each with size and SHA-256.</param>
internal sealed record LocalModelVariant(
    LocalModelFormat Format,
    string FolderName,
    string? DefaultBaseUrl,
    IReadOnlyList<LocalModelFileSpec> Files)
{
    /// <summary>Gets the total size of the files in bytes.</summary>
    public long DownloadBytes => Files.Sum(f => f.Bytes);

    /// <summary>
    /// Gets the message a download gives when the file set has no default address and no mirror is set.
    /// </summary>
    public string NoSourceMessage =>
        $"The local model files '{FolderName}' have no default download address. Set the "
        + $"{EmbeddedEmbeddingProvider.ModelBaseUrlEnvironmentVariable} environment variable to a mirror that serves "
        + $"them as <mirror>/{FolderName}/<file>, or place the files in the model folder yourself.";

    /// <summary>
    /// Gets whether every file is in a folder at exactly its pinned size (the hash is checked separately).
    /// </summary>
    /// <param name="directory">The model folder.</param>
    /// <returns>True when nothing needs downloading.</returns>
    public bool IsOnDisk(string directory) =>
        Files.All(f =>
        {
            var path = Path.Combine(directory, f.FileName);
            return File.Exists(path) && new FileInfo(path).Length == f.Bytes;
        });

    /// <summary>
    /// Gets the files a download fetches, redirected to a mirror when one is given:
    /// <c>&lt;mirror&gt;/&lt;FolderName&gt;/&lt;file&gt;</c>, the convention every non-bge model uses.
    /// </summary>
    /// <param name="mirror">The <c>TECHIERAG_MODEL_BASE_URL</c> value, or null for the default source.</param>
    /// <returns>One entry per file.</returns>
    /// <exception cref="InvalidOperationException">
    /// No mirror is given and the file set has no default address (<see cref="NoSourceMessage"/>).
    /// </exception>
    public IReadOnlyList<ModelDownloadFile> GetDownloadFiles(string? mirror)
    {
        if (string.IsNullOrWhiteSpace(mirror) && DefaultBaseUrl is null)
        {
            throw new InvalidOperationException(NoSourceMessage);
        }

        return Files
            .Select(f => new ModelDownloadFile(
                f.FileName,
                new Uri(string.IsNullOrWhiteSpace(mirror)
                    ? $"{DefaultBaseUrl!.TrimEnd('/')}/{f.RemotePath}"
                    : $"{mirror.TrimEnd('/')}/{FolderName}/{f.FileName}"),
                f.Bytes))
            .ToList();
    }
}
