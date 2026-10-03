namespace TechieRag.Connectors.Email;

/// <summary>One action on one message (REQ-RAG-110 / BRD-169).</summary>
/// <param name="Message">The message to act on.</param>
/// <param name="Kind">What to do.</param>
/// <param name="Target">The destination folder for <see cref="MailActionKind.Move"/>, or the label for
/// <see cref="MailActionKind.AddLabel"/> and <see cref="MailActionKind.RemoveLabel"/>; null for
/// <see cref="MailActionKind.Trash"/>, whose folder the server names.</param>
public sealed record MailAction(MailMessageRef Message, MailActionKind Kind, string? Target = null)
{
    /// <summary>Moves a message to a folder.</summary>
    /// <param name="message">The message.</param>
    /// <param name="folder">The destination folder, spelled as the server lists it. It must exist; it is never created.</param>
    /// <returns>The action.</returns>
    public static MailAction Move(MailMessageRef message, string folder) => new(message, MailActionKind.Move, folder);

    /// <summary>Adds a Gmail label to a message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="label">The label, for example <c>Receipts</c> or the system label <c>\Starred</c>.</param>
    /// <returns>The action.</returns>
    public static MailAction AddLabel(MailMessageRef message, string label) => new(message, MailActionKind.AddLabel, label);

    /// <summary>Removes a Gmail label from a message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="label">The label.</param>
    /// <returns>The action.</returns>
    public static MailAction RemoveLabel(MailMessageRef message, string label) => new(message, MailActionKind.RemoveLabel, label);

    /// <summary>Moves a message to the server's Trash folder. Never a permanent delete.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The action.</returns>
    public static MailAction Trash(MailMessageRef message) => new(message, MailActionKind.Trash);
}
