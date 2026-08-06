namespace TechieRag.Connectors.Confluence;

/// <summary>
/// What to ingest from a Confluence site (REQ-RAG-020 / BRD-64).
/// </summary>
/// <remarks>
/// <para><b>Credentials are inputs.</b> <see cref="ApiToken"/> is supplied by the caller from
/// wherever it already stores secrets — TechieDesk reads it from the OS keychain through its own
/// <c>ISecretStore</c> — and lives in memory for the run only. TechieRag has no secret store and
/// never writes this value to disk, puts it in a URL, or includes it in a log line or an exception
/// message. Do not persist a populated instance of this class.</para>
/// </remarks>
public sealed class ConfluenceConnectorOptions
{
    /// <summary>Gets or sets the site base URL, without a trailing slash.</summary>
    /// <remarks>Required. Cloud sites include the <c>/wiki</c> suffix, e.g. <c>https://acme.atlassian.net/wiki</c>.</remarks>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the space key to ingest.</summary>
    /// <remarks>Ingests every page in the space at any depth. Mutually exclusive with <see cref="RootPageId"/>.</remarks>
    public string? SpaceKey { get; set; }

    /// <summary>Gets or sets the id of a page whose tree to ingest.</summary>
    /// <remarks>Ingests that page and, unless <see cref="IncludeChildPages"/> is off, everything beneath it. Mutually exclusive with <see cref="SpaceKey"/>.</remarks>
    public string? RootPageId { get; set; }

    /// <summary>Gets or sets whether a page tree walk descends past the root page.</summary>
    /// <remarks>Only meaningful with <see cref="RootPageId"/>; a space listing is inherently recursive.</remarks>
    public bool IncludeChildPages { get; set; } = true;

    /// <summary>Gets or sets the account email used for Cloud basic authentication.</summary>
    /// <remarks>
    /// Set this together with <see cref="ApiToken"/> for Atlassian Cloud, which pairs an email with
    /// an API token. Leave it null on Server or Data Center, where <see cref="ApiToken"/> alone is a
    /// personal access token sent as a bearer token.
    /// </remarks>
    public string? UserEmail { get; set; }

    /// <summary>Gets or sets the API token or personal access token.</summary>
    /// <remarks>Null reads anonymously, which only works on a site that publishes the space. Supply this from your own secret store; see the remarks on this class.</remarks>
    public string? ApiToken { get; set; }

    /// <summary>Gets or sets how many pages to request per listing call.</summary>
    /// <remarks>The API caps this well below 100; 25 is its own default and is respected here rather than fought.</remarks>
    public int PageSize { get; set; } = 25;

    /// <summary>Gets the site base URL with any trailing slash removed.</summary>
    /// <returns>An absolute URL with no trailing slash.</returns>
    /// <exception cref="ConnectorException"><see cref="BaseUrl"/> is empty or not an absolute URL.</exception>
    public string ResolveBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || !Uri.IsWellFormedUriString(BaseUrl.TrimEnd('/'), UriKind.Absolute))
        {
            throw new ConnectorException(
                "confluence",
                "A Confluence connector needs an absolute BaseUrl, e.g. 'https://acme.atlassian.net/wiki'.");
        }

        return BaseUrl.TrimEnd('/');
    }
}
