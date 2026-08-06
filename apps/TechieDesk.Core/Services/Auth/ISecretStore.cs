namespace TechieDesk.Services.Auth;

/// <summary>
/// The machine- and user-bound secret store TechieDesk keeps its long-lived credentials in
/// (REQ-FN-039 / BRD-132): AppManager JWT + refresh tokens, and provider API keys.
/// </summary>
/// <remarks>
/// <para><b>Why this seam exists.</b> The real implementation is the operating system's credential
/// store — Keychain on macOS, the Credential Manager / DPAPI on Windows — which
/// <c>Microsoft.Maui.Storage.SecureStorage</c> already wraps. That type lives in the MAUI head;
/// this project is plain <c>net10.0</c> and must never take a MAUI dependency, or the test project
/// could not reference it. So Core states the contract and the head supplies the platform.</para>
/// <para><b>Why the members are synchronous.</b> <see cref="ISessionStore"/> is a synchronous
/// surface with sixteen referencing files, and persistence is an implementation detail of it, not a
/// new call shape for its callers. The platform stores behind this interface are synchronous system
/// calls that MAUI merely presents as tasks, so nothing is lost by expressing them that way here.</para>
/// <para><b>Never a file.</b> An implementation must not fall back to writing a plain file. When the
/// OS store is unavailable the correct degradation is <see cref="EphemeralSecretStore"/> — process
/// memory, <see cref="IsDurable"/> false — which loses the session on exit but never leaves token
/// material on disk.</para>
/// </remarks>
public interface ISecretStore
{
    /// <summary>
    /// Gets a value indicating whether values written here survive the process exiting.
    /// </summary>
    /// <remarks>
    /// False means the OS store was unavailable and secrets are held in memory only. Callers that
    /// would otherwise replace a working on-disk encryption scheme (the provider API keys in
    /// <c>techierag-config.json</c>) MUST check this first, or a restart would make an operator's
    /// saved keys unrecoverable.
    /// </remarks>
    bool IsDurable { get; }

    /// <summary>Reads a stored secret.</summary>
    /// <param name="key">The secret's key.</param>
    /// <returns>The stored value, or null when absent or unreadable.</returns>
    string? Read(string key);

    /// <summary>Writes (or replaces) a secret.</summary>
    /// <param name="key">The secret's key.</param>
    /// <param name="value">The value to store.</param>
    void Write(string key, string value);

    /// <summary>Removes a stored secret.</summary>
    /// <param name="key">The secret's key.</param>
    /// <returns>True when a value was present and removed.</returns>
    bool Delete(string key);
}
