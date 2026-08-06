namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Request body of <c>POST /IssueSvc</c> — a new support issue (REQ-UI-032).
/// </summary>
/// <remarks>
/// The JSON field is <c>type</c>, not <c>issueType</c>: the create body and the read payload
/// disagree on that one name in the published wire contract, and this type follows the wire rather
/// than tidying it.
/// </remarks>
public sealed class CreateIssueRequest
{
    /// <summary>Gets or sets the owning application identifier. Required by the endpoint.</summary>
    public int ApplicationId { get; set; }

    /// <summary>Gets or sets the issue title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the issue description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the issue type (<c>Bug</c>, <c>Feature</c>, <c>Question</c>, …).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets the priority (<c>Low</c>, <c>Medium</c>, <c>High</c>, <c>Critical</c>).</summary>
    public string Priority { get; set; } = string.Empty;
}
