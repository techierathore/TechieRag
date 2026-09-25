namespace TechieRag.Local.Runtime;

/// <summary>One file of a model variant: where it is fetched from, its size and the hash it must have.</summary>
/// <param name="FileName">The file name inside the model folder.</param>
/// <param name="RemotePath">The path under the variant's base URL.</param>
/// <param name="Bytes">The exact size in bytes.</param>
/// <param name="Hash">The lower-case hex hash the downloaded file must have, of the kind <paramref name="HashKind"/> names.</param>
/// <param name="HashKind">
/// How <paramref name="Hash"/> is computed: SHA-256 for every catalogue file and every large file on Hugging Face,
/// the git blob SHA-1 for a small Hugging Face file, the only fingerprint Hugging Face publishes for it (REQ-RAG-108).
/// </param>
internal sealed record LocalModelFileSpec(
    string FileName,
    string RemotePath,
    long Bytes,
    string Hash,
    LocalModelHashKind HashKind = LocalModelHashKind.Sha256);
