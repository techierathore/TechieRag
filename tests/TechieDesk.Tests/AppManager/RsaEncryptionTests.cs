using System.Net;
using System.Text.Json;
using TechieDesk.Services.AppManager;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.AppManager;

/// <summary>
/// REQ-FN-001 / BRD-14: RSA-OAEP-SHA256 password encryption, public-key caching, and the
/// retry-once behaviour on DECRYPTION_FAILED.
/// </summary>
public sealed class RsaEncryptionTests : IDisposable
{
    private readonly RsaKeyFixture keys = new();

    /// <inheritdoc />
    public void Dispose()
    {
        keys.Dispose();
    }

    /// <summary>
    /// Login sends an encryptedPassword field (never a plaintext password) whose base64
    /// ciphertext decrypts with the server's private key under RSA-OAEP-SHA256 back to the
    /// original password — proving algorithm, padding, and encoding.
    /// </summary>
    [Fact]
    public async Task LoginEncryptsPassword()
    {
        var handler = new StubHttpMessageHandler((request, body) =>
            request.RequestUri!.AbsolutePath switch
            {
                "/AuthSvc/public-key" => StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.PublicKeyResponse(keys.PublicKeyPem)),
                "/AuthSvc/login" => StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.LoginResponse()),
                _ => StubHttpMessageHandler.Json(HttpStatusCode.NotFound, TestFactory.ErrorResponse("NOT_FOUND", "no route", 404))
            });
        var client = TestFactory.Client(handler);

        await client.LoginAsync("jane.doe@example.com", "P@ssw0rd!");

        var loginCall = handler.Calls.Single(call => call.PathAndQuery.Split('?')[0] == "/AuthSvc/login");
        using var document = JsonDocument.Parse(loginCall.Body!);
        Assert.False(document.RootElement.TryGetProperty("password", out _));
        var encrypted = document.RootElement.GetProperty("encryptedPassword").GetString();
        Assert.Equal("P@ssw0rd!", keys.Decrypt(encrypted!));
    }

    /// <summary>
    /// The public key is fetched once and cached: two logins produce exactly one
    /// GET /AuthSvc/public-key call.
    /// </summary>
    [Fact]
    public async Task PublicKeyIsCached()
    {
        var handler = new StubHttpMessageHandler((request, body) =>
            request.RequestUri!.AbsolutePath switch
            {
                "/AuthSvc/public-key" => StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.PublicKeyResponse(keys.PublicKeyPem)),
                _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.LoginResponse())
            });
        var client = TestFactory.Client(handler);

        await client.LoginAsync("jane.doe@example.com", "P@ssw0rd!");
        await client.LoginAsync("jane.doe@example.com", "P@ssw0rd!");

        Assert.Equal(1, handler.Calls.Count(call => call.PathAndQuery == "/AuthSvc/public-key"));
    }

    /// <summary>
    /// On DECRYPTION_FAILED the client clears the cached key, refetches it, and retries the
    /// call exactly once — the second attempt succeeds.
    /// </summary>
    [Fact]
    public async Task DecryptionFailedRetriesOnce()
    {
        var loginAttempts = 0;
        var handler = new StubHttpMessageHandler((request, body) =>
        {
            if (request.RequestUri!.AbsolutePath == "/AuthSvc/public-key")
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.PublicKeyResponse(keys.PublicKeyPem));
            }

            loginAttempts++;
            return loginAttempts == 1
                ? StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, TestFactory.ErrorResponse("DECRYPTION_FAILED", "stale key", 400))
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.LoginResponse());
        });
        var client = TestFactory.Client(handler);

        var result = await client.LoginAsync("jane.doe@example.com", "P@ssw0rd!");

        Assert.Equal("jane.doe@example.com", result.Email);
        Assert.Equal(2, loginAttempts);
        Assert.Equal(2, handler.Calls.Count(call => call.PathAndQuery == "/AuthSvc/public-key"));
    }

    /// <summary>
    /// A second consecutive DECRYPTION_FAILED is not retried again: the typed exception
    /// surfaces after exactly two login attempts.
    /// </summary>
    [Fact]
    public async Task DecryptionFailedTwiceThrows()
    {
        var loginAttempts = 0;
        var handler = new StubHttpMessageHandler((request, body) =>
        {
            if (request.RequestUri!.AbsolutePath == "/AuthSvc/public-key")
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.PublicKeyResponse(keys.PublicKeyPem));
            }

            loginAttempts++;
            return StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, TestFactory.ErrorResponse("DECRYPTION_FAILED", "still stale", 400));
        });
        var client = TestFactory.Client(handler);

        var exception = await Assert.ThrowsAsync<AppManagerException>(
            () => client.LoginAsync("jane.doe@example.com", "P@ssw0rd!"));

        Assert.Equal(AppManagerError.DecryptionFailed, exception.Error);
        Assert.Equal(2, loginAttempts);
    }

    /// <summary>
    /// Change-password encrypts both the current and the new password fields; neither is ever
    /// transmitted in plaintext.
    /// </summary>
    [Fact]
    public async Task ChangePasswordEncryptsBothFields()
    {
        var handler = new StubHttpMessageHandler((request, body) =>
            request.RequestUri!.AbsolutePath switch
            {
                "/AuthSvc/public-key" => StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.PublicKeyResponse(keys.PublicKeyPem)),
                _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"success\":true,\"data\":null,\"message\":\"ok\"}")
            });
        var client = TestFactory.Client(handler);

        await client.ChangePasswordAsync("access-token-1", "OldP@ss1!", "NewP@ss2!");

        var call = handler.Calls.Single(recorded => recorded.PathAndQuery == "/UserSvc/change-password");
        using var document = JsonDocument.Parse(call.Body!);
        Assert.Equal("OldP@ss1!", keys.Decrypt(document.RootElement.GetProperty("encryptedCurrentPassword").GetString()!));
        Assert.Equal("NewP@ss2!", keys.Decrypt(document.RootElement.GetProperty("encryptedNewPassword").GetString()!));
    }
}
