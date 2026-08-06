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

    /// <summary>
    /// Gets or sets the localizable form of <see cref="Content"/> — a consumer-defined code plus its
    /// arguments — or null when the text is not the product's own words (REQ-UI-059 / BRD-91).
    /// </summary>
    /// <remarks>
    /// <para><b>Why a transcript needs this at all.</b> A message the PRODUCT authors — "no model is
    /// configured", "that skill was not allowed to run" — used to be localized when it was WRITTEN
    /// and the finished sentence frozen into history. Read a year later in another language, it is
    /// still in the language of the day it happened. The fix is the one this codebase already uses
    /// for run history: persist the code and its arguments, and render at display time.</para>
    /// <para><b><see cref="Content"/> stays the English, deliberately.</b> It is what goes to the
    /// MODEL as conversation history, where a translated sentence would change what the model is
    /// told; it is what a support engineer reads in a database browser; and it is what renders when
    /// this column is null. A row written before this column existed has no code and never will —
    /// printing its stored text verbatim is permanent, not transitional. Deleting that fallback
    /// would blank out a user's real chat history, which is the one outcome the policy forbids.</para>
    /// </remarks>
    public string? ContentJson { get; set; }

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
