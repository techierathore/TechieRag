using TechieDesk.Services.Agents;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 — the <c>web-search</c> skill. It leaves the machine by design, so the properties
/// worth proving are that it only runs when a provider was deliberately configured, and that with
/// no provider it says so rather than looking like a search that found nothing.
/// </summary>
public class WebSearchSkillTests
{
    /// <summary>The skill binds to the catalogue name the toggles and the resolver use.</summary>
    [Fact]
    public void BindsToTheCatalogueName()
    {
        Assert.Equal(SkillCatalog.WebSearch, WebSearchSkill.Create(null).SkillName);
    }

    /// <summary>
    /// With no provider nominated, the tool reports itself unavailable and names what is missing.
    /// The critical part is that it is NOT an empty result list, which would read as "the web has
    /// nothing on this" and is a factual claim the tool has no basis to make.
    /// </summary>
    [Fact]
    public async Task WithNoProviderItReportsUnavailable()
    {
        var result = await WebSearchSkill.Create(null)
            .Invoke("""{"query":"vector databases"}""", CancellationToken.None);

        Assert.True(SkillUnavailable.IsUnavailable(result));
        Assert.Contains("no web search provider is configured", result, StringComparison.Ordinal);
    }

    /// <summary>A provider that is present but unusable explains itself the same honest way.</summary>
    [Fact]
    public async Task AnUnusableProviderReportsItsOwnReason()
    {
        var provider = new FakeWebSearchProvider { IsConfigured = false, UnavailableReason = "no API key saved" };

        var result = await WebSearchSkill.Create(provider)
            .Invoke("""{"query":"vector databases"}""", CancellationToken.None);

        Assert.True(SkillUnavailable.IsUnavailable(result));
        Assert.Contains("no API key saved", result, StringComparison.Ordinal);
    }

    /// <summary>A configured provider receives the query and its results reach the model.</summary>
    [Fact]
    public async Task AConfiguredProviderReturnsFormattedResults()
    {
        var provider = new FakeWebSearchProvider
        {
            Hits = [new WebSearchHit("Qdrant", "https://qdrant.tech", "Vector search engine")]
        };

        var result = await WebSearchSkill.Create(provider)
            .Invoke("""{"query":"vector databases"}""", CancellationToken.None);

        Assert.Equal("vector databases", provider.LastQuery);
        Assert.Contains("Qdrant — https://qdrant.tech", result, StringComparison.Ordinal);
        Assert.Contains("Vector search engine", result, StringComparison.Ordinal);
    }

    /// <summary>A blank query never reaches the provider, so no request leaves the machine.</summary>
    [Fact]
    public async Task ABlankQueryNeverReachesTheProvider()
    {
        var provider = new FakeWebSearchProvider();

        var result = await WebSearchSkill.Create(provider).Invoke("{}", CancellationToken.None);

        Assert.Null(provider.LastQuery);
        Assert.Contains("No query supplied", result, StringComparison.Ordinal);
    }

    /// <summary>A result count the model over-asks for is clamped to the skill's own ceiling.</summary>
    [Fact]
    public async Task AnOversizedResultCountIsClamped()
    {
        var provider = new FakeWebSearchProvider();

        await WebSearchSkill.Create(provider)
            .Invoke("""{"query":"caps","maxResults":500}""", CancellationToken.None);

        Assert.Equal(WebSearchSkill.MaxResults, provider.LastMaxResults);
    }

    /// <summary>An empty result set is reported as empty, never as unavailability.</summary>
    [Fact]
    public async Task NoResultsIsNotTheSameAsUnavailable()
    {
        var result = await WebSearchSkill.Create(new FakeWebSearchProvider())
            .Invoke("""{"query":"nothing at all"}""", CancellationToken.None);

        Assert.False(SkillUnavailable.IsUnavailable(result));
        Assert.Contains("No web results", result, StringComparison.Ordinal);
    }

    /// <summary>A provider the host cannot reach becomes a reportable failure, not a thrown turn.</summary>
    [Fact]
    public async Task AnUnreachableProviderIsReportedNotThrown()
    {
        var provider = new FakeWebSearchProvider { Failure = new HttpRequestException("host is down") };

        var result = await WebSearchSkill.Create(provider)
            .Invoke("""{"query":"caps"}""", CancellationToken.None);

        Assert.Contains("host is down", result, StringComparison.Ordinal);
    }

    /// <summary>A search provider under test control, so no test ever reaches the network.</summary>
    private sealed class FakeWebSearchProvider : IWebSearchProvider
    {
        /// <summary>Gets or sets whether the provider claims to be usable.</summary>
        public bool IsConfigured { get; set; } = true;

        /// <summary>Gets or sets the reason reported when it is not.</summary>
        public string? UnavailableReason { get; set; }

        /// <summary>Gets or sets the hits to return.</summary>
        public IReadOnlyList<WebSearchHit> Hits { get; set; } = [];

        /// <summary>Gets or sets a failure to raise instead of returning hits.</summary>
        public Exception? Failure { get; set; }

        /// <summary>Gets the query the skill passed in, or null when it never called.</summary>
        public string? LastQuery { get; private set; }

        /// <summary>Gets the result count the skill asked for.</summary>
        public int LastMaxResults { get; private set; }

        /// <inheritdoc />
        public Task<IReadOnlyList<WebSearchHit>> SearchAsync(
            string query, int maxResults, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            LastMaxResults = maxResults;
            return Failure is not null ? Task.FromException<IReadOnlyList<WebSearchHit>>(Failure) : Task.FromResult(Hits);
        }
    }
}
