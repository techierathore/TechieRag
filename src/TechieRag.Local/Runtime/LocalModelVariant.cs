using TechieRag.Embedded;

namespace TechieRag.Local.Runtime;

/// <summary>
/// One model in one runtime's format: its folder under the model root, its files and the memory it
/// needs per token of context.
/// </summary>
/// <param name="Format">The format a runtime must load.</param>
/// <param name="FolderName">The folder under the model root, for example <c>qwen2.5-0.5b-instruct-gguf</c>.</param>
/// <param name="DefaultBaseUrl">The base URL files are fetched from, pinned to one repository commit.</param>
/// <param name="Files">The files, each with size and SHA-256.</param>
internal sealed record LocalModelVariant(
    LocalModelFormat Format,
    string FolderName,
    string DefaultBaseUrl,
    IReadOnlyList<LocalModelFileSpec> Files)
{
    /// <summary>Gets the total size of the files in bytes.</summary>
    public long DownloadBytes => Files.Sum(f => f.Bytes);

    /// <summary>
    /// Gets the files a download fetches, redirected to a mirror when one is given:
    /// <c>&lt;mirror&gt;/&lt;FolderName&gt;/&lt;file&gt;</c>, the convention every non-bge model uses.
    /// </summary>
    /// <param name="mirror">The <c>TECHIERAG_MODEL_BASE_URL</c> value, or null for the default source.</param>
    /// <returns>One entry per file.</returns>
    public IReadOnlyList<ModelDownloadFile> GetDownloadFiles(string? mirror) => Files
        .Select(f => new ModelDownloadFile(
            f.FileName,
            new Uri(string.IsNullOrWhiteSpace(mirror)
                ? $"{DefaultBaseUrl.TrimEnd('/')}/{f.RemotePath}"
                : $"{mirror.TrimEnd('/')}/{FolderName}/{f.FileName}"),
            f.Bytes))
        .ToList();
}
