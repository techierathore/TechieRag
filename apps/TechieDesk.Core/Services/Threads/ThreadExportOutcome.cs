using TechieDesk.Services.Files;

namespace TechieDesk.Services.Threads;

/// <summary>
/// What happened when a thread export was requested, and exactly what the UI should tell the
/// user about it (REQ-FN-010, BRD-35).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> This type IS the toast contract. A caller renders a success toast if and
/// only if <see cref="IsSuccess"/> is true, an error toast if and only if <see cref="IsError"/> is
/// true, and nothing at all when the user cancelled the save panel. <see cref="IsSuccess"/> is only
/// ever true when <see cref="ThreadExportService"/> has confirmed a non-empty file exists at
/// <see cref="FilePath"/>, which is what stops the app claiming an export it never wrote.</para>
/// </remarks>
public sealed record ThreadExportOutcome
{
    /// <summary>Gets the outcome of the underlying save.</summary>
    public required FileSaveStatus Status { get; init; }

    /// <summary>Gets the absolute path of the exported file, or null when nothing was written.</summary>
    public string? FilePath { get; init; }

    /// <summary>Gets the number of bytes written, or zero when nothing was written.</summary>
    public long BytesWritten { get; init; }

    /// <summary>Gets the message the UI should show, or an empty string when it should stay silent.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets a value indicating whether a success toast is warranted — true only when a file was
    /// written to disk and verified present.
    /// </summary>
    public bool IsSuccess => Status == FileSaveStatus.Saved;

    /// <summary>
    /// Gets a value indicating whether an error toast is warranted. A cancelled save panel is
    /// neither a success nor an error, so this is false for it.
    /// </summary>
    public bool IsError => Status == FileSaveStatus.Failed;
}
