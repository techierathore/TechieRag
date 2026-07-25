namespace TechieRag.Models;

/// <summary>
/// A persistent conversation thread owned by a user, optionally scoped to a workspace.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Groups persisted chat messages into named, renamable, deletable
/// conversations. Stored in the TrThread table by IConversationStore implementations.</para>
/// </remarks>
public class ConversationThread
{
    /// <summary>Gets or sets the unique thread identifier.</summary>
    public string ThreadId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the owning user identifier.</summary>
    public required string UserId { get; set; }

    /// <summary>Gets or sets the workspace this thread belongs to, or null for the global scope.</summary>
    public string? WorkspaceId { get; set; }

    /// <summary>Gets or sets the thread title shown in thread lists.</summary>
    public string Title { get; set; } = "New Conversation";

    /// <summary>Gets or sets when the thread was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets when the thread was last updated (UTC); bumped on every new message.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
