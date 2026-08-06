namespace TechieDesk.Services.Agents;

/// <summary>
/// One result row returned by a web search provider.
/// </summary>
/// <param name="Title">The result's title.</param>
/// <param name="Url">The absolute URL of the result.</param>
/// <param name="Snippet">The provider's summary of the page, which may be empty.</param>
public sealed record WebSearchHit(string Title, string Url, string Snippet);

/// <summary>
/// The seam the <c>web-search</c> skill queries (BRD-84 / REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>Why this is an interface with no shipped implementation.</b> REQ-NFR-008 makes
/// zero-egress the default posture, and <c>OutboundEgressTests</c> is a structural guard that fails
/// the build when a new outbound HTTP path appears in TechieDesk without review. A search provider
/// is an outbound path to a host the operator has to choose. Shipping one silently would either
/// break that guard or slip past it, so the skill ships with the seam and no provider: the tool is
/// real, and it reports itself <see cref="SkillUnavailable">unavailable</see> until an operator
/// supplies a provider.</para>
/// <para><b>A provider that is present but not usable</b> — no API key, an endpoint that has never
/// been reachable — answers <see cref="IsConfigured"/> false and explains itself through
/// <see cref="UnavailableReason"/>, so the skill still degrades honestly rather than throwing.</para>
/// </remarks>
public interface IWebSearchProvider
{
    /// <summary>Gets whether this provider can actually run a search right now.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Gets why the provider cannot run, in terms the workspace owner can act on, or null when
    /// <see cref="IsConfigured"/> is true.
    /// </summary>
    string? UnavailableReason { get; }

    /// <summary>Runs one search.</summary>
    /// <param name="query">What to search for.</param>
    /// <param name="maxResults">The most results to return.</param>
    /// <param name="cancellationToken">Token to cancel the search.</param>
    /// <returns>The results, best first, or an empty list when the provider found nothing.</returns>
    Task<IReadOnlyList<WebSearchHit>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken = default);
}
