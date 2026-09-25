namespace TechieRag.Local.Runtime;

/// <summary>How a model file's pinned hash is computed (REQ-RAG-061, REQ-RAG-108).</summary>
internal enum LocalModelHashKind
{
    /// <summary>The SHA-256 of the file's bytes.</summary>
    Sha256,

    /// <summary>
    /// Git's blob hash: the SHA-1 of <c>"blob " + size + "\0"</c> followed by the file's bytes. Hugging Face
    /// publishes it (the tree entry's <c>oid</c>) for a small file kept in git rather than in large-file storage.
    /// </summary>
    GitBlobSha1
}
