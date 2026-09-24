namespace TechieRag.Local.Runtime;

/// <summary>One file of a model variant: where it is fetched from, its size and its SHA-256.</summary>
/// <param name="FileName">The file name inside the model folder.</param>
/// <param name="RemotePath">The path under the variant's base URL.</param>
/// <param name="Bytes">The exact size in bytes.</param>
/// <param name="Sha256">The lower-case hex SHA-256 the downloaded file must have.</param>
internal sealed record LocalModelFileSpec(string FileName, string RemotePath, long Bytes, string Sha256);
