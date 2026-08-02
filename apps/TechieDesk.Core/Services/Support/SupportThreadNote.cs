namespace TechieDesk.Services.Support;

/// <summary>
/// Builds the text blocks TechieDesk appends to an issue description or comment (REQ-UI-047).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why text.</b> The AppManager wire contract v1.4 has no attachment-upload endpoint and no
/// priority-mutation endpoint — <c>POST /IssueSvc/{aIssueId}/comments</c> is the only write surface
/// an issue exposes after creation. Rather than invent endpoints the server does not serve, both
/// features are recorded on the thread, which is a real, documented call. The screen says so in
/// plain words instead of implying a file was uploaded or a field was changed server-side.
/// </para>
/// <para>
/// Every method here is pure, so the exact wording a support engineer will read is asserted in
/// tests rather than discovered in production.
/// </para>
/// </remarks>
public static class SupportThreadNote
{
    /// <summary>Heading that opens the attachment manifest.</summary>
    public const string AttachmentHeading = "Attachments (held on the sender's device):";

    /// <summary>
    /// Builds the manifest listing files staged with a description or comment.
    /// </summary>
    /// <param name="attachments">The staged attachments; may be empty.</param>
    /// <returns>The manifest block, or an empty string when there is nothing to list.</returns>
    public static string FormatAttachmentManifest(IReadOnlyList<SupportAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        if (attachments.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string> { AttachmentHeading };
        foreach (var attachment in attachments)
        {
            lines.Add($"- {attachment.FileName} ({attachment.ContentType}, {attachment.FormattedSize})");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Joins a body with its attachment manifest.
    /// </summary>
    /// <param name="body">The user's own text.</param>
    /// <param name="attachments">The staged attachments; may be empty.</param>
    /// <returns>The text to send as the description or comment.</returns>
    public static string Compose(string? body, IReadOnlyList<SupportAttachment> attachments)
    {
        var manifest = FormatAttachmentManifest(attachments);
        var text = (body ?? string.Empty).Trim();

        if (manifest.Length == 0)
        {
            return text;
        }

        return text.Length == 0
            ? manifest
            : text + Environment.NewLine + Environment.NewLine + manifest;
    }

    /// <summary>
    /// Builds the comment recording a priority change (REQ-UI-047).
    /// </summary>
    /// <param name="fromPriority">The priority code the issue carried, or null when unknown.</param>
    /// <param name="toPriority">The priority code the user chose.</param>
    /// <param name="reason">The optional free-text reason.</param>
    /// <returns>The comment body to post to the thread.</returns>
    public static string FormatPriorityChange(string? fromPriority, string toPriority, string? reason)
    {
        // REQ-UI-051: invariant on purpose. This is a comment POSTED TO APPMANAGER and read by a
        // support engineer, not text drawn on this user's screen — a Hindi install must not file a
        // ticket note the person answering it cannot read. It therefore names the wire CODES, which
        // is also what makes the note mean the same thing whoever opens the thread.
        var to = (toPriority ?? string.Empty).Trim();
        var headline = string.IsNullOrWhiteSpace(fromPriority)
            ? $"Priority set to {to}."
            : $"Priority changed from {fromPriority.Trim()} to {to}.";

        var trimmedReason = (reason ?? string.Empty).Trim();
        return trimmedReason.Length == 0
            ? headline
            : headline + Environment.NewLine + $"Reason: {trimmedReason}";
    }
}
