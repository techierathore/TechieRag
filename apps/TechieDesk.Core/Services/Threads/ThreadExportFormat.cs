namespace TechieDesk.Services.Threads;

/// <summary>
/// The document formats a conversation thread can be exported to (REQ-FN-010, BRD-35).
/// </summary>
public enum ThreadExportFormat
{
    /// <summary>A human-readable Markdown transcript.</summary>
    Markdown,

    /// <summary>A machine-readable JSON document.</summary>
    Json
}
