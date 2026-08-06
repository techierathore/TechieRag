namespace TechieDesk.Services.Files;

/// <summary>
/// The outcome of a request to save content to a user-chosen location on disk (REQ-FN-010).
/// </summary>
/// <remarks>
/// Three states, deliberately not two. A cancelled save panel is NOT a failure — the user chose
/// not to save and must not be shown an error — but it is emphatically not a success either, and
/// collapsing it into a boolean is what let the browser-blob export report "Exported" while
/// writing nothing.
/// </remarks>
public enum FileSaveStatus
{
    /// <summary>The content was written to <see cref="FileSaveResult.FilePath"/> and verified present.</summary>
    Saved,

    /// <summary>The user dismissed the save panel; nothing was written and nothing went wrong.</summary>
    Cancelled,

    /// <summary>The save was attempted and did not produce a file; the reason is in the result message.</summary>
    Failed
}
