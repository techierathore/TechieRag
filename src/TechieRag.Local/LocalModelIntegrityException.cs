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
        : base($"The model file '{fileName}' failed its SHA-256 check (expected {expectedSha256}, got {actualSha256}). "
               + "It was deleted; the next attempt downloads it again.")
    {
        FileName = fileName;
        ExpectedSha256 = expectedSha256;
        ActualSha256 = actualSha256;
    }

    /// <summary>Gets the file that failed.</summary>
    public string FileName { get; }

    /// <summary>Gets the hash the file is pinned to.</summary>
    public string ExpectedSha256 { get; }

    /// <summary>Gets the hash the file had.</summary>
    public string ActualSha256 { get; }
}
