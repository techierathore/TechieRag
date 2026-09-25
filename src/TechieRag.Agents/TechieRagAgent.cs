using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TechieRag.Agents.Retrieval;
using TechieRag.Models;
using MeaiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace TechieRag.Agents;

/// <summary>The <see cref="ITechieRagAgent"/> built by <see cref="TechieRagAgentBuilder"/> (REQ-RAG-016 / BRD-84).</summary>
public sealed class TechieRagAgent : ITechieRagAgent
{
    private readonly RetrievalContextProvider retrieval;

    /// <summary>Creates the agent; use <see cref="TechieRagAgentBuilder"/>.</summary>
    /// <param name="agent">The built Agent Framework agent.</param>
    /// <param name="rag">The TechieRag instance.</param>
    /// <param name="retrieval">The retrieval provider the agent was built with.</param>
    internal TechieRagAgent(AIAgent agent, ITechieRag rag, RetrievalContextProvider retrieval)
    {
        Agent = agent;
        Rag = rag;
        this.retrieval = retrieval;
    }

    /// <inheritdoc/>
    public AIAgent Agent { get; }

    /// <inheritdoc/>
    public ITechieRag Rag { get; }

    /// <inheritdoc/>
    public RetrievalContextProvider Retrieval => retrieval;

    /// <inheritdoc/>
    public async Task<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default) =>
        await Agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public Task<AgentRagResponse> AskAsync(string question, AgentSession? session = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        return AskAsync([new MeaiChatMessage(ChatRole.User, question)], session, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AgentRagResponse> AskAsync(IEnumerable<MeaiChatMessage> messages, AgentSession? session = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        session ??= await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        var response = await Agent.RunAsync(messages, session, null, cancellationToken).ConfigureAwait(false);
        var state = retrieval.GetState(session);

        return new AgentRagResponse
        {
            Answer = response.Text,
            Sources = state.Collected,
            Searches = state.Searches,
            PendingApprovals = response.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>().ToList(),
            Raw = response
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<RagStreamEvent> AskStreamAsync(string question, AgentSession? session = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        session ??= await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        var state = retrieval.GetState(session);
        var answer = new StringBuilder();

        // The provider starts a new turn (searches cleared) before the first update, so a tool result
        // is a search's exactly when the turn's search count has grown since the last Sources event;
        // list_documents and any host tool leave it unchanged and raise none.
        var searchesReported = 0;
        await foreach (var update in Agent.RunStreamingAsync(question, session, null, cancellationToken).ConfigureAwait(false))
        {
            if (update.Contents.OfType<FunctionResultContent>().Any() && state.Searches.Count > searchesReported)
            {
                searchesReported = state.Searches.Count;
                yield return RagStreamEvent.FromSources(state.Collected);
            }

            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                answer.Append(text);
                yield return RagStreamEvent.FromToken(text);
            }
        }

        yield return RagStreamEvent.FromCompleted(answer.ToString());
    }
}
