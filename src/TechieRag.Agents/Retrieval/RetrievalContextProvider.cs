using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using TechieRag.Agentic;
using TechieRag.Agents.Interop;
using TechieRag.Services;

namespace TechieRag.Agents.Retrieval;

/// <summary>
/// Supplies the knowledge-base tools of <see cref="KnowledgeBaseTools"/> to an Agent Framework agent,
/// with one <see cref="RetrievalTurnState"/> per <see cref="AgentSession"/> (REQ-RAG-016 / BRD-84).
/// </summary>
/// <remarks>
/// <para>At the start of each run the provider starts a new turn on the session's state (fresh search
/// budget, citation refs kept) and hands the agent <c>search_knowledge_base</c> and
/// <c>list_documents</c> bound to that state. The tools are the core contract behind a
/// <see cref="ToolRegistry"/> exposed through <see cref="ToolHandlerFunctions"/>, so the classic loop
/// and the MAF loop read byte-identical results.</para>
/// <para>State is held in memory against the session object; a session serialized and restored gets a
/// fresh state (refs restart at S1). A run with no session shares one fallback state.</para>
/// </remarks>
public sealed class RetrievalContextProvider : AIContextProvider
{
    private readonly IRetrievalSource source;
    private readonly RetrievalToolOptions options;
    private readonly ConditionalWeakTable<AgentSession, RetrievalTurnState> states = new();
    private readonly RetrievalTurnState sessionlessState = new();

    /// <summary>Creates the provider.</summary>
    /// <param name="source">What the tools search.</param>
    /// <param name="options">Tool options; null uses the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public RetrievalContextProvider(IRetrievalSource source, RetrievalToolOptions? options = null)
        : base(null, null, null)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.source = source;
        this.options = options ?? new RetrievalToolOptions();
    }

    /// <summary>Gets the tool options.</summary>
    public RetrievalToolOptions Options => options;

    /// <summary>Gets the retrieval state of a session: this turn's typed sources and search traces.</summary>
    /// <param name="session">The session, or null for the sessionless state.</param>
    /// <returns>The state.</returns>
    public RetrievalTurnState GetState(AgentSession? session) =>
        session is null ? sessionlessState : states.GetValue(session, _ => new RetrievalTurnState());

    /// <inheritdoc/>
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = GetState(context.Session);
        state.BeginTurn();

        var registry = new ToolRegistry().RegisterKnowledgeBase(source, options, state);
        return ValueTask.FromResult(new AIContext { Tools = ToolHandlerFunctions.From(registry) });
    }
}
