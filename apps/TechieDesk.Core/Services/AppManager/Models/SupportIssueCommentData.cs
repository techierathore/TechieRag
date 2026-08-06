namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// One public comment on a support issue, as returned inside
/// <c>GET /IssueSvc/{aIssueId}</c> (REQ-UI-033).
/// </summary>
/// <remarks>
/// Internal-team comments are never exposed through the external API, so
/// <see cref="IsInternal"/> is carried only to be honest about what the server sent — the screen
/// must not invent an author or a visibility the wire never claimed.
/// </remarks>
public sealed class SupportIssueCommentData
{
    /// <summary>Gets or sets the comment identifier.</summary>
    public int CommentId { get; set; }

    /// <summary>Gets or sets the comment body.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the comment is support-internal.</summary>
    public bool IsInternal { get; set; }

    /// <summary>Gets or sets the display name of whoever wrote the comment.</summary>
    public string? CreatedByName { get; set; }

    /// <summary>Gets or sets when the comment was written.</summary>
    public DateTimeOffset? CreatedDate { get; set; }
}
