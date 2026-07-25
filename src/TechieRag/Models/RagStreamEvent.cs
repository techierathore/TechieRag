namespace TechieRag.Models;

/// <summary>
/// The kind of event emitted by the streaming RAG envelope APIs.
/// </summary>
public enum RagStreamEventType
{
    /// <summary>The retrieved sources for the answer (always the first event).</summary>
    Sources,
    /// <summary>An incremental answer token.</summary>
    Token,
    /// <summary>The stream finished; carries the full aggregated answer.</summary>
    Completed
}

/// <summary>
/// A single event in a streaming RAG response, carrying either the retrieved sources,
/// an answer token, or the completed answer.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets streaming consumers receive the retrieval sources (citations)
/// alongside the token stream. The first event is always <see cref="RagStreamEventType.Sources"/>,
/// followed by zero or more <see cref="RagStreamEventType.Token"/> events, and a final
/// <see cref="RagStreamEventType.Completed"/> event with the aggregated answer.</para>
/// <para><b>Code Flow:</b> Yielded by ITechieRag.AskStreamWithSourcesAsync and
/// ITechieRag.ChatWithRagStreamWithSourcesAsync.</para>
/// </remarks>
public class RagStreamEvent
{
    /// <summary>Gets the event type.</summary>
    public required RagStreamEventType Type { get; init; }

    /// <summary>Gets the answer token for <see cref="RagStreamEventType.Token"/> events; otherwise null.</summary>
    public string? Token { get; init; }

    /// <summary>Gets the retrieved sources for <see cref="RagStreamEventType.Sources"/> events; otherwise null.</summary>
    public IReadOnlyList<SearchResult>? Sources { get; init; }

    /// <summary>Gets the full aggregated answer for <see cref="RagStreamEventType.Completed"/> events; otherwise null.</summary>
    public string? Answer { get; init; }

    /// <summary>
    /// Creates a sources event.
    /// </summary>
    /// <param name="sources">The retrieved search results.</param>
    /// <returns>A <see cref="RagStreamEventType.Sources"/> event.</returns>
    public static RagStreamEvent FromSources(IReadOnlyList<SearchResult> sources) =>
        new() { Type = RagStreamEventType.Sources, Sources = sources };

    /// <summary>
    /// Creates a token event.
    /// </summary>
    /// <param name="token">The incremental answer token.</param>
    /// <returns>A <see cref="RagStreamEventType.Token"/> event.</returns>
    public static RagStreamEvent FromToken(string token) =>
        new() { Type = RagStreamEventType.Token, Token = token };

    /// <summary>
    /// Creates a completion event.
    /// </summary>
    /// <param name="answer">The full aggregated answer.</param>
    /// <returns>A <see cref="RagStreamEventType.Completed"/> event.</returns>
    public static RagStreamEvent FromCompleted(string answer) =>
        new() { Type = RagStreamEventType.Completed, Answer = answer };
}
