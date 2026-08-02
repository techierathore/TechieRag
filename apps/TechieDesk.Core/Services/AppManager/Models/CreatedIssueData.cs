namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Payload of <c>POST /IssueSvc</c> — the identifiers the server assigned to a newly created
/// support issue (REQ-UI-032).
/// </summary>
public sealed class CreatedIssueData
{
    /// <summary>Gets or sets the numeric issue identifier used in URLs.</summary>
    public int IssueId { get; set; }

    /// <summary>Gets or sets the human-facing issue number (e.g. <c>ISS-2026-0005</c>).</summary>
    public string IssueNumber { get; set; } = string.Empty;

    /// <summary>Gets or sets the issue title as recorded.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the status the issue opened in.</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTimeOffset? CreatedDate { get; set; }
}
