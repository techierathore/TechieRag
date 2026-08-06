using TechieRag.Web;

namespace TechieDesk.Services.Agents;

/// <summary>
/// The external things the catalogue skills need in order to do real work (REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>Implemented and configured are different questions,</b> and this type is where the
/// second one is answered. Every catalogue skill has a tool behind it in this build; whether that
/// tool has a search provider, a fetcher, a database or a file area to work against depends on the
/// install. A missing dependency is left null here, and the skill reports itself
/// <see cref="SkillUnavailable">unavailable</see> with the reason when it is called — which is a
/// third state the agent editor's <see cref="AgentSkillAvailability"/> deliberately does not
/// collapse into "not built".</para>
/// <para><b>Nothing here is supplied by the model.</b> Targets and roots are operator
/// configuration, set once and out of band, because an agent that could nominate its own database
/// or its own directory would have no boundary at all.</para>
/// </remarks>
public sealed class WorkspaceSkillOptions
{
    /// <summary>An install with no optional dependency configured — the stock, private posture.</summary>
    public static readonly WorkspaceSkillOptions None = new();

    /// <summary>Gets the search provider the <c>web-search</c> skill queries, or null.</summary>
    public IWebSearchProvider? WebSearch { get; init; }

    /// <summary>Gets the page fetcher the <c>web-scrape</c> skill reads with, or null.</summary>
    public IWebContentFetcher? WebFetcher { get; init; }

    /// <summary>Gets the read-only database the <c>sql-query</c> skill may query, or null.</summary>
    public ISqlQueryTarget? SqlTarget { get; init; }

    /// <summary>Gets the directory the <c>file-operations</c> skill is confined to, or null.</summary>
    public FileOperationsSandbox? Files { get; init; }
}
