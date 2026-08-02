namespace TechieDesk.Services.Connectors;

/// <summary>
/// The connector kinds this build can save and run (REQ-RAG-019, REQ-RAG-020).
/// </summary>
/// <remarks>
/// <para>Each value is the <c>SourceType</c> of the library connector behind it, not a separate app
/// vocabulary. The string is written into <see cref="ConnectorJobPayload.ConnectorType"/>, into the
/// <c>Connector</c> table and onto every ingested document's metadata, so it is persisted three times
/// over and must stay stable between releases.</para>
/// </remarks>
public static class ConnectorTypes
{
    /// <summary>A GitHub or GitLab repository, read on one branch through the host's REST API.</summary>
    public const string Repository = "repository";

    /// <summary>A Confluence space or page tree, read through the Confluence REST API.</summary>
    public const string Confluence = "confluence";

    /// <summary>A mailbox, read over IMAP-with-TLS or from a local <c>.mbox</c> archive.</summary>
    /// <remarks>
    /// REQ-RAG-049 / BRD-135. The string matches <c>EmailConnector.SourceType</c> in the library, as
    /// every value here must.
    /// </remarks>
    public const string Email = "email";

    /// <summary>Gets the descriptors the connector hub lists as "sources you can add".</summary>
    /// <remarks>
    /// REQ-UI-051 / BRD-91: the second and third columns are RESOURCE KEYS, never English. This table
    /// is read by the connector hub's source grid AND by the connector editor's type chooser, so an
    /// English literal here rendered English on a Hindi install in two places at once and no razor
    /// counter could see either — the table lives in a static service class.
    /// </remarks>
    public static IReadOnlyList<ConnectorTypeDescriptor> All { get; } =
    [
        new ConnectorTypeDescriptor(
            Repository, "ConnectorTypeRepositoryName", "ConnectorTypeRepositoryDescription"),
        new ConnectorTypeDescriptor(
            Confluence, "ConnectorTypeConfluenceName", "ConnectorTypeConfluenceDescription"),
        new ConnectorTypeDescriptor(
            Email, "ConnectorTypeEmailName", "ConnectorTypeEmailDescription"),
    ];

    /// <summary>Determines whether a stored type string names a connector this build can run.</summary>
    /// <param name="connectorType">The type key to check.</param>
    /// <returns><see langword="true"/> when the type is known.</returns>
    public static bool IsKnown(string? connectorType) =>
        connectorType is not null
        && All.Any(type => type.ConnectorType.Equals(connectorType, StringComparison.Ordinal));

    /// <summary>Gets the resource key naming a connector type, for a grid column or a badge.</summary>
    /// <param name="connectorType">The stored type key.</param>
    /// <returns>The key, or <see langword="null"/> when this build does not know the type.</returns>
    /// <remarks>
    /// Null rather than a substitute key on purpose. A connector row written by a newer build has a
    /// type this one cannot name, and the honest rendering is the stored type key itself — which the
    /// caller already holds — rather than a translated label for something else.
    /// </remarks>
    public static string? DisplayNameKey(string? connectorType) =>
        connectorType is null
            ? null
            : All.FirstOrDefault(type => type.ConnectorType.Equals(connectorType, StringComparison.Ordinal))
                ?.DisplayNameKey;
}
