namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Payload of <c>GET /AuthSvc/public-key</c> — the server's RSA public key used for
/// client-side password encryption (RSA-OAEP-SHA256).
/// </summary>
public sealed class PublicKeyData
{
    /// <summary>Gets or sets the PEM-encoded RSA public key.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the encryption algorithm identifier (e.g. <c>RSA-OAEP-256</c>).</summary>
    public string? Algorithm { get; set; }

    /// <summary>Gets or sets the ciphertext encoding (e.g. <c>base64</c>).</summary>
    public string? Encoding { get; set; }
}
