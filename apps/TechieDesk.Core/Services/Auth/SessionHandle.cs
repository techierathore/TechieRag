using System.Buffers.Text;
using System.Security.Cryptography;

namespace TechieDesk.Services.Auth;

/// <summary>
/// Generates the opaque session handles stored in the <see cref="SessionCookie.Name"/> cookie
/// (REQ-FN-032).
/// </summary>
/// <remarks>
/// A handle is 256 bits of cryptographically secure randomness, base64url-encoded. It is a pure
/// lookup key: it encodes nothing about the user, carries no token material, and is worthless
/// without the server-side <see cref="ISessionStore"/> entry it points at. A fresh handle is
/// minted on every successful login so a pre-seeded handle can never be promoted to an
/// authenticated session (session-fixation defence).
/// </remarks>
public static class SessionHandle
{
    /// <summary>The number of random bytes in a handle (256 bits).</summary>
    public const int SizeInBytes = 32;

    /// <summary>
    /// Creates a new opaque session handle.
    /// </summary>
    /// <returns>A base64url-encoded 256-bit random handle.</returns>
    public static string Create()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SizeInBytes));
    }
}
