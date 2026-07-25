using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.Auth;

namespace TechieDesk.Tests.Support;

/// <summary>
/// Shared builders for AppManager client tests: options, clients, RSA key pairs, and
/// wire-format JSON payloads.
/// </summary>
public static class TestFactory
{
    /// <summary>Builds default test options pointing at a fake AppManager host.</summary>
    /// <returns>The options instance.</returns>
    public static AppManagerOptions DefaultOptions()
    {
        return new AppManagerOptions
        {
            BaseUrl = "https://appmanager.test",
            ApiKey = "ak_test_key",
            ApiSecret = "test_secret",
            ApplicationId = 7,
            TokenRefreshLeadSeconds = 120
        };
    }

    /// <summary>Builds an <see cref="AppManagerClient"/> over a stub handler.</summary>
    /// <param name="handler">The stub message handler.</param>
    /// <param name="options">Optional options override.</param>
    /// <param name="cache">Optional shared public-key cache.</param>
    /// <returns>The client.</returns>
    public static AppManagerClient Client(
        StubHttpMessageHandler handler,
        AppManagerOptions? options = null,
        IPublicKeyCache? cache = null)
    {
        return new AppManagerClient(
            new HttpClient(handler),
            Options.Create(options ?? DefaultOptions()),
            cache ?? new PublicKeyCache(),
            NullLogger<AppManagerClient>.Instance);
    }

    /// <summary>Builds an auth-mode provider for the given mode.</summary>
    /// <param name="appManagerEnabled">True for AppManager mode, false for offline.</param>
    /// <returns>The mode provider.</returns>
    public static ITechieDeskAuthModeProvider Mode(bool appManagerEnabled)
    {
        var options = appManagerEnabled
            ? DefaultOptions()
            : new AppManagerOptions { BaseUrl = string.Empty };
        return new TechieDeskAuthModeProvider(Options.Create(options));
    }

    /// <summary>Serializes the standard public-key success envelope.</summary>
    /// <param name="publicKeyPem">The PEM public key to embed.</param>
    /// <returns>The JSON string.</returns>
    public static string PublicKeyResponse(string publicKeyPem)
    {
        return JsonSerializer.Serialize(new
        {
            success = true,
            data = new { publicKey = publicKeyPem, algorithm = "RSA-OAEP-256", encoding = "base64" },
            message = "Use this public key to encrypt passwords before sending"
        });
    }

    /// <summary>Serializes a login/register success envelope.</summary>
    /// <param name="applicationRole">The app-scoped role to return.</param>
    /// <param name="expiresAt">Optional token expiry (defaults to one hour from now).</param>
    /// <returns>The JSON string.</returns>
    public static string LoginResponse(string applicationRole = "User", DateTimeOffset? expiresAt = null)
    {
        return JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                userId = 123,
                email = "jane.doe@example.com",
                firstName = "Jane",
                lastName = "Doe",
                applicationRole,
                appManagerRole = "ApplicationUser",
                isEmailVerified = true,
                accessToken = "access-token-1",
                refreshToken = "refresh-token-1",
                tokenExpiresAt = (expiresAt ?? DateTimeOffset.UtcNow.AddHours(1)).ToString("O")
            },
            message = "Login successful"
        });
    }

    /// <summary>Serializes a refresh success envelope.</summary>
    /// <param name="accessToken">The new access token.</param>
    /// <param name="refreshToken">The new refresh token.</param>
    /// <returns>The JSON string.</returns>
    public static string RefreshResponse(string accessToken = "access-token-2", string refreshToken = "refresh-token-2")
    {
        return JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                accessToken,
                refreshToken,
                expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O")
            },
            message = "Token refreshed successfully"
        });
    }

    /// <summary>Serializes the standard error envelope.</summary>
    /// <param name="errorCode">The wire error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status echoed in the envelope.</param>
    /// <returns>The JSON string.</returns>
    public static string ErrorResponse(string errorCode, string message, int statusCode)
    {
        return JsonSerializer.Serialize(new
        {
            success = false,
            error = errorCode,
            message,
            statusCode,
            traceId = "trace-1"
        });
    }
}
