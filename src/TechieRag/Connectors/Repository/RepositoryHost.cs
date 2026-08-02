namespace TechieRag.Connectors.Repository;

/// <summary>
/// Which hosted-repository REST API a <see cref="RepositoryConnector"/> speaks (REQ-RAG-019 / BRD-63).
/// </summary>
/// <remarks>
/// The two APIs are close enough to share a connector and different enough that the difference
/// cannot be hidden: they disagree on how a project is addressed, how a tree is paged, how a blob is
/// requested, and which header carries the token. This enum is where that disagreement is named
/// once, instead of leaking into every method as an untyped string.
/// </remarks>
public enum RepositoryHost
{
    /// <summary>The GitHub REST API, and GitHub Enterprise Server via a custom base URL.</summary>
    /// <remarks>Addresses projects as <c>owner/repo</c>, returns the whole tree in one recursive response, and authenticates with <c>Authorization: Bearer</c>.</remarks>
    GitHub = 0,

    /// <summary>The GitLab REST API v4, and self-managed GitLab via a custom base URL.</summary>
    /// <remarks>Addresses projects as a URL-encoded path, pages the tree, and authenticates with <c>PRIVATE-TOKEN</c>.</remarks>
    GitLab = 1,
}
