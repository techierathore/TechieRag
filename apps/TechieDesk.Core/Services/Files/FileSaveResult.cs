namespace TechieDesk.Services.Files;

/// <summary>
/// The result of an <see cref="IFileSaveService"/> save request — where the file landed, or why
/// it did not (REQ-FN-010).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives callers a three-way answer (saved / cancelled / failed) so a UI can
/// raise a success toast ONLY for a real write. The previous export path had no result type at all:
/// it fired a WebView blob download and unconditionally toasted success.</para>
/// </remarks>
public sealed record FileSaveResult
{
    /// <summary>Gets the outcome of the save request.</summary>
    public required FileSaveStatus Status { get; init; }

    /// <summary>Gets the absolute path of the written file, or null when nothing was written.</summary>
    public string? FilePath { get; init; }

    /// <summary>Gets the number of bytes written, or zero when nothing was written.</summary>
    public long BytesWritten { get; init; }

    /// <summary>Gets the failure reason, or null when the save succeeded or was cancelled.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets a value indicating whether a file was actually written.</summary>
    public bool IsSaved => Status == FileSaveStatus.Saved;

    /// <summary>
    /// Creates a successful result for a file that has been written to disk.
    /// </summary>
    /// <param name="filePath">The absolute path of the written file.</param>
    /// <param name="bytesWritten">The size of the written file in bytes.</param>
    /// <returns>A <see cref="FileSaveStatus.Saved"/> result.</returns>
    public static FileSaveResult Saved(string filePath, long bytesWritten) =>
        new() { Status = FileSaveStatus.Saved, FilePath = filePath, BytesWritten = bytesWritten };

    /// <summary>
    /// Creates a result for a save panel the user dismissed.
    /// </summary>
    /// <returns>A <see cref="FileSaveStatus.Cancelled"/> result.</returns>
    public static FileSaveResult Cancelled() => new() { Status = FileSaveStatus.Cancelled };

    /// <summary>
    /// Creates a result for a save that was attempted and did not produce a file.
    /// </summary>
    /// <param name="errorMessage">A user-facing explanation of why the save failed.</param>
    /// <returns>A <see cref="FileSaveStatus.Failed"/> result.</returns>
    public static FileSaveResult Failed(string errorMessage) =>
        new() { Status = FileSaveStatus.Failed, ErrorMessage = errorMessage };
}
