namespace TechieRag.Models;

/// <summary>
/// A chat message persisted in a conversation thread, including any retrieval sources
/// (citations) that were used to generate it.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Round-trips chat history through the TrMessage table so past
/// conversations can be re-rendered with their citations after an application restart.</para>
/// </remarks>
public class StoredChatMessage
{
    /// <summary>Gets or sets the unique message identifier.</summary>
    public string MessageId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the thread this message belongs to.</summary>
    public required string ThreadId { get; set; }

    /// <summary>Gets or sets the message role ("system", "user", "assistant", or "tool").</summary>
    public required string Role { get; set; }

    /// <summary>Gets or sets the message text content.</summary>
    public string? Content { get; set; }

    /// <summary>Gets or sets the retrieval sources cited by this message, or null when none.</summary>
    public IReadOnlyList<SearchResult>? Sources { get; set; }

    /// <summary>Gets or sets when the message was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Converts this stored message to a plain <see cref="ChatMessage"/> for prompt building.
    /// </summary>
    /// <returns>An equivalent chat message.</returns>
    public ChatMessage ToChatMessage() => new() { Role = Role, Content = Content };
}
