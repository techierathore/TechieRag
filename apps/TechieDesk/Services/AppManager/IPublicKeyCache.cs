namespace TechieDesk.Services.AppManager;

/// <summary>
/// Process-wide cache for the AppManager RSA public key used to encrypt password fields
/// (BRD-14). The key only changes when the server rotates its encryption keys, so it is
/// fetched once and invalidated only on a <c>DECRYPTION_FAILED</c> response.
/// </summary>
public interface IPublicKeyCache
{
    /// <summary>Gets the cached PEM-encoded public key, or null when not yet fetched.</summary>
    string? PublicKeyPem { get; }

    /// <summary>Stores a freshly fetched PEM-encoded public key.</summary>
    /// <param name="publicKeyPem">The PEM-encoded RSA public key.</param>
    void Set(string publicKeyPem);

    /// <summary>Clears the cached key so the next call refetches it from the server.</summary>
    void Clear();
}
