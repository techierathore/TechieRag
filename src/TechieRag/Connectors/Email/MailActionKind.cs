namespace TechieRag.Connectors.Email;

/// <summary>What a mail action does to a message (REQ-RAG-110 / BRD-169).</summary>
public enum MailActionKind
{
    /// <summary>Move the message to another folder.</summary>
    Move,

    /// <summary>Add a Gmail label to the message.</summary>
    AddLabel,

    /// <summary>Remove a Gmail label from the message.</summary>
    RemoveLabel,

    /// <summary>Move the message to the server's Trash folder. Never a permanent delete.</summary>
    Trash,
}
