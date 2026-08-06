namespace TechieRag.Web;

/// <summary>
/// Fetches a URL and returns the page (REQ-RAG-031 / BRD-112).
/// </summary>
/// <remarks>
/// A seam, not ceremony: the crawler's real logic is budget, depth, de-duplication and host policy,
/// and none of that should need a network to test. Every crawler test in this library drives a fake
/// implementation of this interface.
/// </remarks>
public interface IWebContentFetcher
{
    /// <summary>Fetches and parses a single page.</summary>
    /// <param name="url">Absolute http/https URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed page.</returns>
    /// <exception cref="WebFetchException">The page could not be fetched or was not HTML.</exception>
    Task<WebPage> FetchAsync(string url, CancellationToken cancellationToken = default);
}
