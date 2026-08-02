namespace TechieDesk.Services.AppManager;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IPublicKeyCache"/>, registered as a
/// singleton so every <see cref="AppManagerClient"/> instance shares one cached key.
/// </summary>
public sealed class PublicKeyCache : IPublicKeyCache
{
    private volatile string? publicKeyPem;

    /// <inheritdoc />
    public string? PublicKeyPem => publicKeyPem;

    /// <inheritdoc />
    public void Set(string publicKeyPem)
    {
        this.publicKeyPem = publicKeyPem;
    }

    /// <inheritdoc />
    public void Clear()
    {
        publicKeyPem = null;
    }
}
