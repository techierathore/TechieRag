using System.Globalization;
using TechieRag.Web;

namespace TechieDesk.Services.Agents;

/// <summary>
/// The <c>web-scrape</c> catalogue skill as a library tool (BRD-84 / REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>It reuses the library's fetcher, deliberately.</b> <see cref="IWebContentFetcher"/> is
/// the same seam the web ingestion crawler uses (REQ-RAG-031), which means this skill inherits the
/// SSRF guard, the HTML-only content check and the readable-text extraction that were already
/// reviewed there. Writing a second HTTP path for agents would have created a second thing to get
/// right, and a second entry on the REQ-NFR-008 egress allow-list.</para>
/// <para><b>Opt-in by construction.</b> The catalogue ships the skill off and marks it
/// <see cref="SkillExposure.LeavesMachine"/>; an unpermitted skill is never registered, so a stock
/// install still fetches nothing.</para>
/// <para><b>A fetch that fails is not the same as a skill that is unavailable.</b> A page that will
/// not load reports the failure in its own words so the model can try a different URL; only a
/// missing fetcher yields <see cref="SkillUnavailable"/>.</para>
/// </remarks>
public static class WebScrapeSkill
{
    /// <summary>The JSON Schema for the web-scrape tool's parameters.</summary>
    public const string Schema =
        """{"type":"object","properties":{"url":{"type":"string","description":"Absolute http or https URL of the page to read"},"maxCharacters":{"type":"integer","description":"How much of the page text to return, 500 to 20000","default":6000}},"required":["url"]}""";

    /// <summary>The description the model is shown.</summary>
    public const string Description =
        "Fetches one web page and returns its readable text with navigation and scripts stripped. "
        + "This request leaves the machine.";

    /// <summary>The most page text returned in one call, in characters.</summary>
    public const int MaxCharacters = 20000;

    /// <summary>The amount of page text returned when the model does not choose.</summary>
    public const int DefaultCharacters = 6000;

    /// <summary>
    /// Binds the web-scrape skill to a page fetcher.
    /// </summary>
    /// <param name="fetcher">
    /// The fetcher to read pages with, or null when this install has none configured.
    /// </param>
    /// <returns>The skill implementation.</returns>
    public static SkillImplementation Create(IWebContentFetcher? fetcher) =>
        new(SkillCatalog.WebScrape, Description, Schema,
            (argumentsJson, cancellationToken) => RunAsync(fetcher, argumentsJson, cancellationToken));

    /// <summary>Runs one scrape call.</summary>
    /// <param name="fetcher">The page fetcher, or null.</param>
    /// <param name="argumentsJson">The tool-call arguments.</param>
    /// <param name="cancellationToken">Token to cancel the fetch.</param>
    /// <returns>The page text, a refusal, or an unavailability report.</returns>
    private static async Task<string> RunAsync(
        IWebContentFetcher? fetcher, string argumentsJson, CancellationToken cancellationToken)
    {
        if (fetcher is null)
        {
            return SkillUnavailable.Because(
                "no web fetcher is configured on this install, so pages cannot be read.");
        }

        var url = SkillArguments.ReadString(argumentsJson, "url");
        var refusal = RefuseUrl(url);
        if (refusal is not null)
        {
            return refusal;
        }

        var budget = SkillArguments.ReadInt(
            argumentsJson, "maxCharacters", DefaultCharacters, 500, MaxCharacters);

        try
        {
            var page = await fetcher.FetchAsync(url, cancellationToken).ConfigureAwait(false);
            return Format(page, budget);
        }
        catch (WebFetchException ex)
        {
            return $"The page could not be read: {ex.Message}";
        }
    }

    /// <summary>Rejects anything that is not an absolute http or https URL.</summary>
    /// <param name="url">The URL from the tool call.</param>
    /// <returns>The refusal, or null when the URL is usable.</returns>
    /// <remarks>
    /// The scheme check is here as well as in the fetcher because <c>file:</c> and <c>ftp:</c> are
    /// the shapes a prompt-injected instruction would reach for, and refusing them before a request
    /// is composed keeps the reason legible in the trace.
    /// </remarks>
    private static string? RefuseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "No URL supplied, so nothing was fetched.";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return $"Refused: '{url}' is not an absolute URL.";
        }

        return parsed.Scheme is "http" or "https"
            ? null
            : $"Refused: only http and https pages can be read, not '{parsed.Scheme}'.";
    }

    /// <summary>Renders the fetched page for the model, truncating honestly.</summary>
    /// <param name="page">The fetched page.</param>
    /// <param name="budget">The most characters of body text to include.</param>
    /// <returns>The formatted page text.</returns>
    private static string Format(WebPage page, int budget)
    {
        var body = (page.Text ?? string.Empty).Trim();
        var isTruncated = body.Length > budget;
        if (isTruncated)
        {
            body = body[..budget];
        }

        var header = string.Format(
            CultureInfo.InvariantCulture, "{0}\n{1}\n\n", page.Title, page.FinalUrl);

        return isTruncated
            ? header + body + $"\n\n[truncated at {budget} characters]"
            : header + body;
    }
}
