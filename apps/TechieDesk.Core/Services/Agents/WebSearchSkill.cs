using System.Globalization;
using System.Text;

namespace TechieDesk.Services.Agents;

/// <summary>
/// The <c>web-search</c> catalogue skill as a library tool (BRD-84 / REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>Opt-in by construction.</b> The catalogue ships this skill off
/// (<see cref="SkillCatalog.Skills"/>, <c>DefaultEnabled: false</c>, exposure
/// <see cref="SkillExposure.LeavesMachine"/>) and <see cref="AgentToolPlanner"/> registers only
/// permitted skills, so on a stock install the tool is never offered to the model and no request
/// can leave the machine. That is what keeps REQ-NFR-008's zero-egress default intact while still
/// letting a workspace owner turn searching on deliberately.</para>
/// <para><b>No provider ships.</b> See <see cref="IWebSearchProvider"/>: choosing a search host is
/// the operator's call, so with no provider configured this tool reports itself unavailable and
/// explains why, rather than returning nothing and looking like a search that found nothing.</para>
/// </remarks>
public static class WebSearchSkill
{
    /// <summary>The JSON Schema for the web-search tool's parameters.</summary>
    public const string Schema =
        """{"type":"object","properties":{"query":{"type":"string","description":"What to search the web for"},"maxResults":{"type":"integer","description":"How many results to return, 1 to 10","default":5}},"required":["query"]}""";

    /// <summary>The description the model is shown.</summary>
    public const string Description =
        "Searches the public web through the workspace's configured search provider and returns "
        + "titles, URLs and snippets. This request leaves the machine.";

    /// <summary>The most results this tool will ever return in one call.</summary>
    public const int MaxResults = 10;

    /// <summary>The number of results returned when the model does not choose.</summary>
    public const int DefaultResults = 5;

    /// <summary>
    /// Binds the web-search skill to a provider.
    /// </summary>
    /// <param name="provider">
    /// The configured search provider, or null when the workspace has nominated none.
    /// </param>
    /// <returns>The skill implementation.</returns>
    public static SkillImplementation Create(IWebSearchProvider? provider) =>
        new(SkillCatalog.WebSearch, Description, Schema,
            (argumentsJson, cancellationToken) => RunAsync(provider, argumentsJson, cancellationToken));

    /// <summary>Runs one search call.</summary>
    /// <param name="provider">The search provider, or null.</param>
    /// <param name="argumentsJson">The tool-call arguments.</param>
    /// <param name="cancellationToken">Token to cancel the search.</param>
    /// <returns>The formatted results, a refusal, or an unavailability report.</returns>
    private static async Task<SkillOutcome> RunAsync(
        IWebSearchProvider? provider, string argumentsJson, CancellationToken cancellationToken)
    {
        if (provider is null)
        {
            return SkillUnavailable.Coded("SkillUnavailableWebSearchNoProvider");
        }

        if (!provider.IsConfigured)
        {
            // The provider's own words stay as it gave them; only our default is coded.
            return provider.UnavailableReason is { Length: > 0 } reason
                ? SkillUnavailable.Because(reason)
                : SkillUnavailable.Coded("SkillUnavailableWebSearchBroken");
        }

        var query = SkillArguments.ReadString(argumentsJson, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return "No query supplied, so nothing was searched.";
        }

        var wanted = SkillArguments.ReadInt(argumentsJson, "maxResults", DefaultResults, 1, MaxResults);

        try
        {
            var hits = await provider.SearchAsync(query, wanted, cancellationToken).ConfigureAwait(false);
            return Format(query, hits);
        }
        catch (HttpRequestException ex)
        {
            return $"The search provider could not be reached: {ex.Message}";
        }
    }

    /// <summary>Renders the results the way the model reads them best.</summary>
    /// <param name="query">The query that was run, echoed so the model can tell calls apart.</param>
    /// <param name="hits">The provider's results.</param>
    /// <returns>The formatted result text.</returns>
    private static string Format(string query, IReadOnlyList<WebSearchHit>? hits)
    {
        if (hits is null || hits.Count == 0)
        {
            return $"No web results for '{query}'.";
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{hits.Count} web result(s) for '{query}':");

        for (var index = 0; index < hits.Count; index++)
        {
            var hit = hits[index];
            text.AppendLine();
            text.Append(CultureInfo.InvariantCulture, $"{index + 1}. {hit.Title} — {hit.Url}");
            if (!string.IsNullOrWhiteSpace(hit.Snippet))
            {
                text.AppendLine();
                text.Append(CultureInfo.InvariantCulture, $"   {hit.Snippet.Trim()}");
            }
        }

        return text.ToString();
    }
}
