using System.Collections.Concurrent;

namespace TechieDesk.Services.Auth;

/// <summary>
/// In-memory <see cref="ISecretStore"/>: the deliberate degradation when no OS credential store is
/// reachable (REQ-FN-039).
/// </summary>
/// <remarks>
/// Used by the test project, and by the desktop head when Keychain / the Credential Manager refuses
/// the process (an unsigned or un-entitled build). It reports <see cref="IsDurable"/> false, which is
/// the whole point: a session simply does not survive a restart, and the provider-key protector keeps
/// its existing on-disk scheme instead of writing references that could never be resolved again.
/// <para>
/// It writes nothing anywhere, so the REQ-FN-039 clause "nothing sensitive is readable from a plain
/// file" holds under this implementation too — what is lost is persistence, never confidentiality.
/// </para>
/// </remarks>
public sealed class EphemeralSecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> secrets = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsDurable => false;

    /// <inheritdoc />
    public string? Read(string key) => secrets.TryGetValue(key, out var value) ? value : null;

    /// <inheritdoc />
    public void Write(string key, string value) => secrets[key] = value;

    /// <inheritdoc />
    public bool Delete(string key) => secrets.TryRemove(key, out _);
}
