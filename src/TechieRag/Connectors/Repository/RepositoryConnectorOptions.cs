namespace TechieRag.Connectors.Repository;

/// <summary>
/// What to ingest from a hosted repository (REQ-RAG-019 / BRD-63).
/// </summary>
/// <remarks>
/// <para><b>The token is an input, not something this library keeps.</b>
/// <see cref="AccessToken"/> is supplied by the caller from wherever it already stores secrets —
/// TechieDesk reads it from the OS keychain through its own <c>ISecretStore</c> — and lives in
/// memory for the run and no longer. TechieRag has no secret store, does not want one, and never
/// writes this value to disk, puts it in a URL, or includes it in a log line or an exception
/// message. Callers should not persist a populated instance of this class.</para>
/// </remarks>
public sealed class RepositoryConnectorOptions
{
    /// <summary>Gets or sets which host API to speak.</summary>
    public RepositoryHost Host { get; set; } = RepositoryHost.GitHub;

    /// <summary>Gets or sets the project as <c>owner/repository</c> (or <c>group/subgroup/project</c>).</summary>
    /// <remarks>Required. Written the same way for both hosts; the connector encodes it as each API needs.</remarks>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the branch, tag or commit to read.</summary>
    /// <remarks>
    /// Null asks the host for the project's default branch, which is the right behaviour: hard-coding
    /// a fallback of "main" would silently fail on every repository still on "master" or using a
    /// release branch, and would report it as an empty repository rather than a wrong branch.
    /// </remarks>
    public string? Branch { get; set; }

    /// <summary>Gets or sets the API base URL, without a trailing slash.</summary>
    /// <remarks>Null uses the public host. Set this for GitHub Enterprise Server or self-managed GitLab.</remarks>
    public string? ApiBaseUrl { get; set; }

    /// <summary>Gets or sets the web base URL used to build citation links, without a trailing slash.</summary>
    /// <remarks>Null uses the public host. Citations must point at a page a human can open, not at the API.</remarks>
    public string? WebBaseUrl { get; set; }

    /// <summary>Gets or sets the access token used to authenticate.</summary>
    /// <remarks>
    /// Null reads anonymously, which works for public repositories at a far lower rate limit. Supply
    /// this from your own secret store; see the remarks on this class.
    /// </remarks>
    public string? AccessToken { get; set; }

    /// <summary>Gets or sets glob patterns a file path must match to be ingested.</summary>
    /// <remarks>
    /// Empty includes everything, which is almost never what anyone means: a repository is mostly
    /// lockfiles, fixtures and build output, and ingesting all of it buries the prose. See
    /// <see cref="GlobFilter"/> for the pattern semantics.
    /// </remarks>
    public IList<string> IncludeGlobs { get; set; } = [];

    /// <summary>Gets or sets glob patterns that exclude a file path outright.</summary>
    /// <remarks>Applied after <see cref="IncludeGlobs"/>; an exclude always wins.</remarks>
    public IList<string> ExcludeGlobs { get; set; } = [];

    /// <summary>Gets or sets how many tree entries to request per page.</summary>
    /// <remarks>Honoured by GitLab, which pages its tree. GitHub returns the whole recursive tree in one response and ignores this.</remarks>
    public int PageSize { get; set; } = 100;

    /// <summary>Gets the API base URL for the configured host, with any override applied.</summary>
    /// <returns>An absolute URL with no trailing slash.</returns>
    public string ResolveApiBaseUrl() =>
        string.IsNullOrWhiteSpace(ApiBaseUrl)
            ? Host == RepositoryHost.GitHub ? "https://api.github.com" : "https://gitlab.com/api/v4"
            : ApiBaseUrl.TrimEnd('/');

    /// <summary>Gets the web base URL for the configured host, with any override applied.</summary>
    /// <returns>An absolute URL with no trailing slash.</returns>
    public string ResolveWebBaseUrl() =>
        string.IsNullOrWhiteSpace(WebBaseUrl)
            ? Host == RepositoryHost.GitHub ? "https://github.com" : "https://gitlab.com"
            : WebBaseUrl.TrimEnd('/');
}
