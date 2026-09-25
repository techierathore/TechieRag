using Microsoft.Agents.AI;
using TechieRag.Agents.Retrieval;
using TechieRag.Models;
using MeaiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace TechieRag.Agents;

/// <summary>
/// A Microsoft Agent Framework agent that answers from a TechieRag knowledge base (REQ-RAG-016 / BRD-84).
/// </summary>
/// <remarks>Agent Framework types are exposed on purpose rather than wrapped: <see cref="Agent"/> gives
/// middleware, <c>AsAIFunction()</c>, workflows and hosting without a parallel abstraction.</remarks>
public interface ITechieRagAgent
{
    /// <summary>Gets the Agent Framework agent.</summary>
    AIAgent Agent { get; }

    /// <summary>Gets the TechieRag instance the agent answers from.</summary>
    ITechieRag Rag { get; }

    /// <summary>
    /// Gets the retrieval provider the agent was built with, for a host that reads a session's retrieval
    /// state (<see cref="RetrievalContextProvider.GetState"/>) or the tool options without casting.
    /// </summary>
    RetrievalContextProvider Retrieval { get; }

    /// <summary>Creates a conversation session.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session.</returns>
    Task<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks one question.</summary>
    /// <param name="question">The user's question.</param>
    /// <param name="session">The conversation, or null for a one-off turn.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The answer with its typed sources and search traces.</returns>
    Task<AgentRagResponse> AskAsync(string question, AgentSession? session = null, CancellationToken cancellationToken = default);

    /// <summary>Runs one turn over messages the host supplies (for example prior turns it persisted itself).</summary>
    /// <param name="messages">The messages for this turn.</param>
    /// <param name="session">The conversation, or null for a one-off turn.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The answer with its typed sources and search traces.</returns>
    Task<AgentRagResponse> AskAsync(IEnumerable<MeaiChatMessage> messages, AgentSession? session = null, CancellationToken cancellationToken = default);

    /// <summary>Asks one question and streams the turn.</summary>
    /// <param name="question">The user's question.</param>
    /// <param name="session">The conversation, or null for a one-off turn.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="RagStreamEventType.Sources"/> event after each <c>search_knowledge_base</c> call
    /// (the turn's sources so far; no other tool call raises one), <see cref="RagStreamEventType.Token"/> events
    /// as text arrives, and one <see cref="RagStreamEventType.Completed"/> last.</returns>
    IAsyncEnumerable<RagStreamEvent> AskStreamAsync(string question, AgentSession? session = null, CancellationToken cancellationToken = default);
}
