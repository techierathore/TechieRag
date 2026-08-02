namespace TechieDesk.Services.Connectors;

/// <summary>
/// Where a connector's access token lives — which is never the application database
/// (REQ-FN-039 applied to REQ-RAG-019 / REQ-RAG-020).
/// </summary>
/// <remarks>
/// <para><b>A GitHub personal access token and a Confluence API token are the same class of secret as
/// a provider API key.</b> They are replayable outbound credentials that frequently carry write scope
/// over an organisation's source code or wiki, so they go where REQ-FN-039 already puts the app's
/// other long-lived secrets: the OS credential store, bound to this machine and this user account.
/// The <c>Connector</c> row keeps only a NAME saying that a credential exists — the same shape
/// <c>techierag-config.json</c> uses for provider keys, where the file holds
/// <c>enc:v2:&lt;field&gt;</c> and no key material at all.</para>
/// <para><b>Nothing behind this interface may write a token in cleartext, anywhere.</b> Not the
/// database, not a settings file, not a log line, not an exception message. When no durable platform
/// store is reachable the degradation is defined and visible through <see cref="IsDurable"/> — never
/// a plaintext file that "works for now".</para>
/// </remarks>
public interface IConnectorSecretStore
{
    /// <summary>Gets a value indicating whether a stored token survives the process exiting.</summary>
    /// <remarks>
    /// False on an unsigned or un-entitled desktop build, where the platform keychain refuses the
    /// process. The connector hub must say so: a token that vanishes on restart is a connector that
    /// starts failing overnight, and the operator needs to be told that before they schedule it.
    /// </remarks>
    bool IsDurable { get; }

    /// <summary>
    /// Gets the RESOURCE KEY of the one-line, operator-facing description of where tokens are kept.
    /// </summary>
    /// <remarks>
    /// <para>Names the mechanism, never a key's material, a path's contents, or a value.</para>
    /// <para><b>REQ-UI-051 / BRD-91 — this member was the worst instance of the defect in the whole
    /// connector cluster.</b> It used to return an English sentence, and the connector hub and the
    /// connector editor interpolated it RAW into four otherwise fully localized alerts. A Hindi
    /// install therefore rendered a Devanagari alert title above an English body, and neither razor
    /// counter could see it, because the sentence is built in a service. Returning a key makes that
    /// impossible rather than merely discouraged: the page has nothing to render but a key, so
    /// forgetting to resolve it fails visibly instead of silently.</para>
    /// </remarks>
    string StorageDescriptionKey { get; }

    /// <summary>Reads one connector's token.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <returns>The token, or <see langword="null"/> when there is none or it could not be resolved.</returns>
    string? Read(string connectorId);

    /// <summary>Stores (or replaces) one connector's token.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <param name="secret">The token. Empty or whitespace removes the stored value.</param>
    void Write(string connectorId, string? secret);

    /// <summary>Removes one connector's token.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <returns><see langword="true"/> when a value was present and removed.</returns>
    bool Delete(string connectorId);
}
