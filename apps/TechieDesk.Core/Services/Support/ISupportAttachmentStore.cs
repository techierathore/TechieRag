namespace TechieDesk.Services.Support;

/// <summary>
/// Stages support-issue attachments on disk, under the one data directory (REQ-UI-047 /
/// REQ-FN-037).
/// </summary>
/// <remarks>
/// Attachments are held per <i>draft</i> — one new-issue form, or one comment composer — so
/// abandoning a draft leaves nothing behind and two composers cannot collide.
/// </remarks>
public interface ISupportAttachmentStore
{
    /// <summary>Gets the absolute directory every staged attachment lives under.</summary>
    string RootDirectory { get; }

    /// <summary>Starts a new draft and returns its key.</summary>
    /// <returns>An opaque key to pass to the other members.</returns>
    string BeginDraft();

    /// <summary>
    /// Validates a file against <see cref="SupportAttachmentPolicy"/> and stages it.
    /// </summary>
    /// <param name="draftKey">The draft the attachment belongs to.</param>
    /// <param name="fileName">The offered file name.</param>
    /// <param name="contentType">The offered MIME type, or null to infer it from the extension.</param>
    /// <param name="content">The file bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The staged attachment, with its measured size and on-disk path.</returns>
    /// <exception cref="SupportAttachmentRejectedException">When the file breaks the type or size rule.</exception>
    Task<SupportAttachment> SaveAsync(
        string draftKey,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one staged attachment.</summary>
    /// <param name="attachment">The attachment to delete.</param>
    void Remove(SupportAttachment attachment);

    /// <summary>Removes every attachment staged for a draft, and the draft's folder.</summary>
    /// <param name="draftKey">The draft to discard.</param>
    void DiscardDraft(string draftKey);
}
