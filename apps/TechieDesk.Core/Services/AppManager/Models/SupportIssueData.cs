namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Payload of <c>GET /IssueSvc</c> (list) and <c>GET /IssueSvc/{aIssueId}</c> (detail) — one
/// support issue raised by the authenticated user (REQ-UI-032, REQ-UI-033).
/// </summary>
/// <remarks>
/// One type serves both endpoints because the wire contract returns the same object: the detail
/// call simply populates <see cref="Comments"/>, which the list call omits. Modelling them
/// separately would mean two shapes that must be kept identical by hand.
/// </remarks>
public sealed class SupportIssueData
{
    /// <summary>Gets or sets the numeric issue identifier used in URLs.</summary>
    public int IssueId { get; set; }

    /// <summary>Gets or sets the human-facing issue number (e.g. <c>ISS-2026-0007</c>).</summary>
    public string IssueNumber { get; set; } = string.Empty;

    /// <summary>Gets or sets the issue title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the issue description as first submitted.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the issue type (<c>Bug</c>, <c>Feature</c>, <c>Question</c>, …).</summary>
    public string? IssueType { get; set; }

    /// <summary>Gets or sets the priority (<c>Low</c>, <c>Medium</c>, <c>High</c>, <c>Critical</c>).</summary>
    public string? Priority { get; set; }

    /// <summary>Gets or sets the status (<c>Open</c>, <c>InProgress</c>, <c>Resolved</c>, <c>Closed</c>).</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the owning application identifier.</summary>
    public int ApplicationId { get; set; }

    /// <summary>Gets or sets the owning application's display name.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTimeOffset? CreatedDate { get; set; }

    /// <summary>Gets or sets the last-update timestamp.</summary>
    public DateTimeOffset? UpdatedDate { get; set; }

    /// <summary>Gets or sets the resolution timestamp, or null while unresolved.</summary>
    public DateTimeOffset? ResolvedDate { get; set; }

    /// <summary>Gets or sets the public comment thread; empty on the list endpoint.</summary>
    public IReadOnlyList<SupportIssueCommentData> Comments { get; set; } =
        Array.Empty<SupportIssueCommentData>();
}
