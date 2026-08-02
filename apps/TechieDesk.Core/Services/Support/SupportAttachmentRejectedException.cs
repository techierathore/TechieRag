namespace TechieDesk.Services.Support;

/// <summary>
/// Thrown when a file offered as a support attachment fails
/// <see cref="SupportAttachmentPolicy"/> (REQ-UI-047).
/// </summary>
/// <remarks>
/// Carries the message the screen shows verbatim: a rejected attachment must say which file and
/// which rule, never a bare "upload failed".
/// </remarks>
public sealed class SupportAttachmentRejectedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="SupportAttachmentRejectedException"/> class.</summary>
    /// <param name="message">The user-facing rejection reason.</param>
    public SupportAttachmentRejectedException(string message)
        : base(message)
    {
    }
}
