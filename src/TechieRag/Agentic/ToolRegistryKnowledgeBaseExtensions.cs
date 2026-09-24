using TechieRag.Services;

namespace TechieRag.Agentic;

/// <summary>Binds the knowledge-base tools to the classic agent loop (REQ-RAG-015 / BRD-83).</summary>
public static class ToolRegistryKnowledgeBaseExtensions
{
    /// <summary>
    /// Registers <c>search_knowledge_base</c> and <c>list_documents</c> on a <see cref="ToolRegistry"/>,
    /// so <see cref="AgentLoopRunner"/>, flows and any <c>IToolHandler</c> consumer can use them.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="source">What the tools search.</param>
    /// <param name="options">Tool options; null uses the defaults.</param>
    /// <param name="state">The conversation's retrieval state; call <see cref="RetrievalTurnState.BeginTurn"/> per user turn.</param>
    /// <returns>The same registry, for chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static ToolRegistry RegisterKnowledgeBase(
        this ToolRegistry registry,
        IRetrievalSource source,
        RetrievalToolOptions? options,
        RetrievalTurnState state)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(state);

        var resolved = options ?? new RetrievalToolOptions();
        var search = KnowledgeBaseTools.SearchDefinition(resolved);
        var list = KnowledgeBaseTools.ListDefinition(resolved);

        registry.Register(search.Name, search.Description, search.ParametersSchema,
            (argumentsJson, cancellationToken) => KnowledgeBaseTools.ExecuteSearchAsync(source, resolved, state, argumentsJson, cancellationToken));
        registry.Register(list.Name, list.Description, list.ParametersSchema,
            (_, cancellationToken) => KnowledgeBaseTools.ExecuteListAsync(source, cancellationToken));

        return registry;
    }
}
