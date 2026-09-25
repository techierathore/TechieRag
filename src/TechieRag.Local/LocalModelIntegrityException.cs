namespace TechieRag.Local;

/// <summary>
/// Thrown when a downloaded model file does not have the SHA-256 it is pinned to; the file has been
/// deleted so the next attempt fetches it again (REQ-RAG-061 / BRD-100).
/// </summary>
public sealed class LocalModelIntegrityException : IOException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="fileName">The file that failed.</param>
    /// <param name="expectedSha256">The hash it is pinned to.</param>
    /// <param name="actualSha256">The hash it had.</param>
    public LocalModelIntegrityException(string fileName, string expectedSha256, string actualSha256)
        : this(fileName, expectedSha256, actualSha256, "SHA-256")
    {
    }

    /// <summary>Creates the exception for a check of a named kind (REQ-RAG-108).</summary>
    /// <param name="fileName">The file that failed.</param>
    /// <param name="expectedHash">The hash it is pinned to.</param>
    /// <param name="actualHash">The hash it had.</param>
    /// <param name="hashKind">The check, for example <c>SHA-256</c> or <c>git blob SHA-1</c> (Hugging Face's fingerprint of a small file).</param>
    public LocalModelIntegrityException(string fileName, string expectedHash, string actualHash, string hashKind)
        : base($"The model file '{fileName}' failed its {hashKind} check (expected {expectedHash}, got {actualHash}). "
               + "It was deleted; the next attempt downloads it again.")
    {
        FileName = fileName;
        ExpectedSha256 = expectedHash;
        ActualSha256 = actualHash;
        HashKind = hashKind;
    }

    /// <summary>Gets the file that failed.</summary>
    public string FileName { get; }

    /// <summary>Gets the check that failed: <c>SHA-256</c>, or <c>git blob SHA-1</c> for a small Hugging Face file.</summary>
    public string HashKind { get; }

    /// <summary>Gets the hash the file is pinned to (of the kind <see cref="HashKind"/> names).</summary>
    public string ExpectedSha256 { get; }

    /// <summary>Gets the hash the file had (of the kind <see cref="HashKind"/> names).</summary>
    public string ActualSha256 { get; }
}
