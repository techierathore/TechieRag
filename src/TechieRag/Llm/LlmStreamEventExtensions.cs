using System.Runtime.CompilerServices;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>Helpers over a typed event stream from <c>ILlmProvider.ChatStreamEventsAsync</c> (REQ-RAG-067).</summary>
public static class LlmStreamEventExtensions
{
    /// <summary>Projects a typed event stream to its text deltas only.</summary>
    /// <param name="events">The typed events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The text fragments, in order; tool calls and the completed event are dropped.</returns>
    /// <remarks>This is how every built-in provider implements <c>ChatStreamAsync</c>, so the two
    /// methods can never disagree about the text.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public static async IAsyncEnumerable<string> ToTextStreamAsync(
        this IAsyncEnumerable<LlmStreamEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        await foreach (var streamEvent in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (streamEvent.Kind == LlmStreamEventKind.TextDelta && !string.IsNullOrEmpty(streamEvent.Text))
            {
                yield return streamEvent.Text;
            }
        }
    }
}
