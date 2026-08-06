namespace TechieDesk.Services.Agents;

/// <summary>
/// The skill implementations this build ships, bound to real work (REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>All six catalogue skills have a tool behind them.</b> RAG search is bound by the caller
/// because it needs the turn's retrieval scope; the other five are self-contained and come from
/// <see cref="Standard"/>. <see cref="ImplementedSkillNames"/> is DERIVED from those factories
/// rather than hand-written, so the list the agent editor renders cannot drift away from what this
/// build can actually execute — the drift the catalogue-versus-implementation tripwire exists to
/// catch.</para>
/// <para><b>Implemented is not the same as configured.</b> Web search needs a provider, SQL query
/// needs a nominated database, file operations needs a file area. Where one is missing the tool
/// still exists and still runs; it returns <see cref="SkillUnavailable"/> with the reason. That is
/// the honest third state, and it is why none of these is stubbed with plausible text — a stub is
/// indistinguishable from a real answer, which is the one failure mode a citation-first product
/// cannot afford.</para>
/// <para><b>Argument parsing lives in <see cref="SkillArguments"/>,</b> not in the page, so a
/// malformed tool-call payload is handled in one tested place instead of in a Razor expression.</para>
/// </remarks>
public static class WorkspaceSkillTools
{
    /// <summary>The JSON Schema for the RAG-search tool's parameters.</summary>
    public const string RagSearchSchema =
        """{"type":"object","properties":{"query":{"type":"string","description":"What to look for in the workspace's documents"}},"required":["query"]}""";

    /// <summary>
    /// The catalogue skills this build can execute. Derived from the factories below, so adding a
    /// name here without an implementation is not possible.
    /// </summary>
    public static readonly IReadOnlyList<string> ImplementedSkillNames = AgentToolPlanner.ImplementedNames(
        All(static (_, _) => Task.FromResult(string.Empty), WorkspaceSkillOptions.None));

    /// <summary>
    /// Binds the RAG-search skill to a workspace-scoped search, honoring whatever retrieval scope
    /// the caller has already resolved for the turn.
    /// </summary>
    /// <param name="search">
    /// Runs the search for a query and returns the passages to hand back to the model.
    /// </param>
    /// <returns>The skill implementation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="search"/> is null.</exception>
    public static SkillImplementation RagSearch(Func<string, CancellationToken, Task<string>> search)
    {
        ArgumentNullException.ThrowIfNull(search);

        return new SkillImplementation(
            SkillCatalog.RagSearch,
            "Searches this workspace's documents and returns the matching passages, honoring the "
                + "retrieval scope chosen for this turn.",
            RagSearchSchema,
            async (argumentsJson, cancellationToken) =>
            {
                var query = ReadString(argumentsJson, "query");
                return string.IsNullOrWhiteSpace(query)
                    ? "No query supplied, so nothing was searched."
                    : await search(query, cancellationToken).ConfigureAwait(false);
            });
    }

    /// <summary>
    /// Builds the five catalogue skills that need nothing from the calling page.
    /// </summary>
    /// <param name="options">
    /// The dependencies this install has configured. Pass <see cref="WorkspaceSkillOptions.None"/>
    /// for a stock install: the tools are still built, and each reports honestly why it cannot run.
    /// </param>
    /// <returns>The web-search, web-scrape, sql-query, chart-generate and file-operations skills.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public static IReadOnlyList<SkillImplementation> Standard(WorkspaceSkillOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return
        [
            WebSearchSkill.Create(options.WebSearch),
            WebScrapeSkill.Create(options.WebFetcher),
            SqlQuerySkill.Create(options.SqlTarget),
            ChartGenerateSkill.Create(),
            FileOperationsSkill.Create(options.Files)
        ];
    }

    /// <summary>
    /// Builds every catalogue skill for one agent turn: RAG search bound to this turn's retrieval,
    /// plus the five standard skills.
    /// </summary>
    /// <param name="search">Runs the workspace search for the RAG-search skill.</param>
    /// <param name="options">The dependencies this install has configured.</param>
    /// <returns>The complete implementation set to hand to <see cref="AgentToolPlanner"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    public static IReadOnlyList<SkillImplementation> All(
        Func<string, CancellationToken, Task<string>> search, WorkspaceSkillOptions options)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(options);

        return [RagSearch(search), .. Standard(options)];
    }

    /// <summary>
    /// Reads a string property out of a tool-call argument payload.
    /// </summary>
    /// <param name="json">The raw JSON arguments produced by the model.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value, or an empty string when absent, null, or unparseable.</returns>
    /// <remarks>
    /// A model can emit malformed JSON. That is a bad tool call, not a crash: returning empty lets
    /// the tool report "no query supplied" into the trace, which the agent loop can act on.
    /// </remarks>
    public static string ReadString(string? json, string property) =>
        SkillArguments.ReadString(json, property);
}
