namespace TechieRag.Connectors.Http;

/// <summary>
/// The single network seam every REST-backed connector goes through (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// The same seam <c>IWebContentFetcher</c> is for the crawler, and for the same reason: what is
/// actually worth testing in a connector is its paging, its filtering, its incremental sync and its
/// error mapping, and none of that should need a network — or a live account, or a valid token — to
/// prove. Every connector test in this library drives a fake implementation of this interface.
/// </remarks>
public interface IConnectorTransport
{
    /// <summary>Performs one GET request.</summary>
    /// <param name="request">The URL and headers to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status, body and headers. Non-2xx statuses are returned, not thrown, because connectors map them to different meanings.</returns>
    /// <exception cref="ConnectorException">The host could not be reached at all.</exception>
    Task<ConnectorHttpResponse> GetAsync(ConnectorHttpRequest request, CancellationToken cancellationToken = default);
}
