using System.Security.Cryptography;
using System.Text;

namespace TechieDesk.Tests.Support;

/// <summary>
/// Generates an RSA key pair per test so encrypted passwords can be decrypted and verified.
/// </summary>
public sealed class RsaKeyFixture : IDisposable
{
    private readonly RSA rsa = RSA.Create(2048);

    /// <summary>Gets the PEM-encoded public key (SubjectPublicKeyInfo).</summary>
    public string PublicKeyPem => rsa.ExportSubjectPublicKeyInfoPem();

    /// <summary>
    /// Decrypts a base64 RSA-OAEP-SHA256 ciphertext produced by the client.
    /// </summary>
    /// <param name="base64Ciphertext">The base64 ciphertext.</param>
    /// <returns>The plaintext string.</returns>
    public string Decrypt(string base64Ciphertext)
    {
        var plainBytes = rsa.Decrypt(Convert.FromBase64String(base64Ciphertext), RSAEncryptionPadding.OaepSHA256);
        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        rsa.Dispose();
    }
}
