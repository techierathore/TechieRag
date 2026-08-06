namespace TechieDesk.Services.Agents;

/// <summary>
/// How far outside the workspace a skill can reach, which is what decides whether it may ship
/// enabled by default (BRD-84 / REQ-RAG-022).
/// </summary>
public enum SkillExposure
{
    /// <summary>Runs entirely on this machine against this workspace's own data.</summary>
    Local,

    /// <summary>Sends something to a third party — the request leaves the machine.</summary>
    LeavesMachine,

    /// <summary>Reaches beyond the workspace in a way the owner should approve deliberately.</summary>
    NeedsReview
}

/// <summary>
/// One entry in the workspace skill catalogue: a library tool the agent loop may be offered.
/// </summary>
/// <param name="Name">The tool name handed to the LLM and stored in the toggle tables.</param>
/// <param name="DisplayNameKey">Resource key for the label shown on the catalogue and agent editor.</param>
/// <param name="DescriptionKey">Resource key for the one-line explanation shown under the label.</param>
/// <param name="Exposure">How far outside the workspace the skill reaches.</param>
/// <param name="DefaultEnabled">Whether a workspace that has never been configured permits it.</param>
/// <remarks>
/// REQ-UI-051: <paramref name="Name"/> is WIRE vocabulary — it is handed to the LLM as a tool name
/// and stored in the per-workspace toggle tables — so it stays culture-invariant. The two display
/// members are resource keys, so this record cannot carry English to a screen.
/// </remarks>
public sealed record SkillDefinition(
    string Name,
    string DisplayNameKey,
    string DescriptionKey,
    SkillExposure Exposure,
    bool DefaultEnabled);

/// <summary>
/// The fixed catalogue of skills a workspace can permit (BRD-84 / REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> every skill is a library <c>IToolHandler</c> tool — the app contributes the
/// registry, the per-workspace toggles and the trace, never the tool semantics. This type is the
/// single place the six catalogue entries are named, so the toggle screen, the agent editor and the
/// run-time intersection cannot drift apart.</para>
/// <para><b>Defaults:</b> only <see cref="RagSearch"/> ships on. Everything that leaves the machine
/// or reaches past the workspace stays off until the owner turns it on deliberately, which is the
/// rule stated in the Agents screen design.</para>
/// <para><b>Availability is a separate question from permission.</b> A skill can be permitted by the
/// catalogue and still have no implementation on this install; see
/// <see cref="AgentToolPlanner"/>, which registers only the intersection of permitted AND
/// implemented. The catalogue never pretends a tool exists.</para>
/// </remarks>
public static class SkillCatalog
{
    /// <summary>Searches this workspace's documents and cites them.</summary>
    public const string RagSearch = "rag-search";

    /// <summary>Queries a search provider for current information.</summary>
    public const string WebSearch = "web-search";

    /// <summary>Fetches and cleans a specific URL on demand.</summary>
    public const string WebScrape = "web-scrape";

    /// <summary>Runs read-only queries against a configured database.</summary>
    public const string SqlQuery = "sql-query";

    /// <summary>Renders a chart from retrieved data.</summary>
    public const string ChartGenerate = "chart-generate";

    /// <summary>Reads and writes files inside the data directory only.</summary>
    public const string FileOperations = "file-operations";

    /// <summary>The catalogue, in the order the Skills tab lists it.</summary>
    /// <remarks>
    /// REQ-UI-051: the second and third columns are RESOURCE KEYS. They previously held English,
    /// and the agents screen mapped the tool name back onto a key in its own <c>@code</c> block —
    /// which left a fallback arm that rendered this table's English for any skill the page's switch
    /// did not know, and two call sites that never went through the switch at all.
    /// </remarks>
    public static readonly IReadOnlyList<SkillDefinition> Skills =
    [
        new(RagSearch, "AgentsSkillRagSearchName", "AgentsSkillRagSearchDescription",
            SkillExposure.Local, DefaultEnabled: true),
        new(WebSearch, "AgentsSkillWebSearchName", "AgentsSkillWebSearchDescription",
            SkillExposure.LeavesMachine, DefaultEnabled: false),
        new(WebScrape, "AgentsSkillWebScrapeName", "AgentsSkillWebScrapeDescription",
            SkillExposure.LeavesMachine, DefaultEnabled: false),
        new(SqlQuery, "AgentsSkillSqlQueryName", "AgentsSkillSqlQueryDescription",
            SkillExposure.NeedsReview, DefaultEnabled: false),
        new(ChartGenerate, "AgentsSkillChartGenerateName", "AgentsSkillChartGenerateDescription",
            SkillExposure.Local, DefaultEnabled: false),
        new(FileOperations, "AgentsSkillFileOperationsName", "AgentsSkillFileOperationsDescription",
            SkillExposure.NeedsReview, DefaultEnabled: false)
    ];

    /// <summary>Finds a catalogue entry by tool name.</summary>
    /// <param name="name">The tool name.</param>
    /// <returns>The definition, or null when the name is not in the catalogue.</returns>
    public static SkillDefinition? Find(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Skills.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets whether a tool name belongs to the catalogue.</summary>
    /// <param name="name">The tool name.</param>
    /// <returns>True when the catalogue defines it.</returns>
    public static bool Contains(string? name) => Find(name) is not null;

    /// <summary>
    /// Builds the catalogue a workspace has when nothing has been toggled — the shipped defaults.
    /// </summary>
    /// <returns>A skill-name to enabled map covering every catalogue entry.</returns>
    public static Dictionary<string, bool> Defaults() =>
        Skills.ToDictionary(s => s.Name, s => s.DefaultEnabled, StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the resource key for the badge describing a skill's exposure.</summary>
    /// <param name="exposure">The exposure to describe.</param>
    /// <returns>The resource key for the badge shown beside the skill.</returns>
    /// <remarks>REQ-UI-051: a key, so the badge cannot be English on a translated install.</remarks>
    public static string ExposureLabelKey(SkillExposure exposure) => exposure switch
    {
        SkillExposure.LeavesMachine => "AgentsExposureLeavesMachine",
        SkillExposure.NeedsReview => "AgentsExposureNeedsReview",
        _ => "AgentsExposureLocal"
    };
}
