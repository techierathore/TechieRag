using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager.Models;

namespace TechieDesk.Services.AppManager;

/// <summary>
/// Typed HTTP client implementation of <see cref="IAppManagerClient"/> for the AppManager API
/// (wire contract v1.4). All AppManager traffic in TechieDesk flows through this class.
/// </summary>
/// <remarks>
/// Sends <c>X-Api-Key</c>/<c>X-Api-Secret</c> headers on every call (BRD-21). Every password
/// field is RSA-encrypted with the cached server public key using RSA-OAEP-SHA256 before
/// transmission (BRD-14); on a <c>DECRYPTION_FAILED</c> response the key is refetched and the
/// call retried exactly once. URL-bound parameters use the v1.4 <c>a</c>-prefixed names
/// (<c>aApplicationId</c>, <c>aFeatureCode</c>); JSON body field names are unchanged. All
/// documented error codes surface as <see cref="AppManagerException"/> with a typed
/// <see cref="AppManagerError"/>.
/// </remarks>
public sealed class AppManagerClient : IAppManagerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient httpClient;
    private readonly IPublicKeyCache publicKeyCache;
    private readonly AppManagerOptions options;
    private readonly ILogger<AppManagerClient> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppManagerClient"/> class.
    /// </summary>
    /// <param name="httpClient">The underlying HTTP client (typed-client registration).</param>
    /// <param name="options">The AppManager configuration (base URL and API credentials).</param>
    /// <param name="publicKeyCache">The shared RSA public-key cache.</param>
    /// <param name="logger">Logger.</param>
    public AppManagerClient(
        HttpClient httpClient,
        IOptions<AppManagerOptions> options,
        IPublicKeyCache publicKeyCache,
        ILogger<AppManagerClient> logger)
    {
        this.httpClient = httpClient;
        this.publicKeyCache = publicKeyCache;
        this.options = options.Value;
        this.logger = logger;

        if (this.httpClient.BaseAddress == null && this.options.IsConfigured)
        {
            this.httpClient.BaseAddress = new Uri(this.options.BaseUrl, UriKind.Absolute);
        }
    }

    /// <summary>
    /// Encrypts a password with the given PEM public key using RSA-OAEP-SHA256 and returns
    /// the base64-encoded ciphertext, per the AppManager password-encryption contract.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="publicKeyPem">The server's PEM-encoded RSA public key.</param>
    /// <returns>Base64-encoded RSA-OAEP-SHA256 ciphertext.</returns>
    public static string EncryptPassword(string password, string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var encryptedBytes = rsa.Encrypt(
            Encoding.UTF8.GetBytes(password),
            RSAEncryptionPadding.OaepSHA256);
        return Convert.ToBase64String(encryptedBytes);
    }

    /// <inheritdoc />
    public async Task<string> GetPublicKeyAsync(CancellationToken cancellationToken = default)
    {
        var cached = publicKeyCache.PublicKeyPem;
        if (!string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        var data = await SendAsync<PublicKeyData>(
            HttpMethod.Get, "/AuthSvc/public-key", null, null, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(data.PublicKey))
        {
            throw new AppManagerException("KEY_FETCH_FAILED", "AppManager returned an empty public key");
        }

        publicKeyCache.Set(data.PublicKey);
        return data.PublicKey;
    }

    /// <inheritdoc />
    public Task<AuthResponseData> RegisterAsync(RegisterRequest request, string password, CancellationToken cancellationToken = default)
    {
        return SendWithPasswordRetryAsync(
            keyPem => SendAsync<AuthResponseData>(HttpMethod.Post, "/AuthSvc/register", new
            {
                email = request.Email,
                encryptedPassword = EncryptPassword(password, keyPem),
                firstName = request.FirstName,
                lastName = request.LastName,
                mobileNumber = request.MobileNumber,
                applicationRoleCode = request.ApplicationRoleCode
            }, null, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AuthResponseData> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        return SendWithPasswordRetryAsync(
            keyPem => SendAsync<AuthResponseData>(HttpMethod.Post, "/AuthSvc/login", new
            {
                email,
                encryptedPassword = EncryptPassword(password, keyPem)
            }, null, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<TokenRefreshData> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        return SendAsync<TokenRefreshData>(
            HttpMethod.Post, "/AuthSvc/refresh", new { refreshToken }, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task LogoutAsync(string accessToken, string? refreshToken, bool logoutAllDevices = false, CancellationToken cancellationToken = default)
    {
        return SendVoidAsync(
            HttpMethod.Post, "/AuthSvc/logout",
            new { refreshToken, logoutAllDevices }, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        return SendVoidAsync(
            HttpMethod.Post, "/AuthSvc/forgot-password", new { email }, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        await SendWithPasswordRetryAsync(
            async keyPem =>
            {
                await SendVoidAsync(HttpMethod.Post, "/AuthSvc/reset-password", new
                {
                    token,
                    encryptedNewPassword = EncryptPassword(newPassword, keyPem)
                }, null, cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        await SendWithPasswordRetryAsync(
            async keyPem =>
            {
                await SendVoidAsync(HttpMethod.Post, "/UserSvc/change-password", new
                {
                    encryptedCurrentPassword = EncryptPassword(currentPassword, keyPem),
                    encryptedNewPassword = EncryptPassword(newPassword, keyPem)
                }, accessToken, cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<UserProfileData> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        return SendAsync<UserProfileData>(
            HttpMethod.Get, "/UserSvc/profile", null, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateProfileAsync(string accessToken, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        return SendVoidAsync(
            HttpMethod.Put, "/UserSvc/profile", request, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public Task<LicenseValidationData> ValidateLicenseAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var url = "/LicenseSvc/validate";
        if (options.ApplicationId.HasValue)
        {
            url += $"?aApplicationId={options.ApplicationId.Value}";
        }

        return SendAsync<LicenseValidationData>(HttpMethod.Post, url, null, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FeatureAccessData> CheckFeatureAsync(string accessToken, string featureCode, CancellationToken cancellationToken = default)
    {
        return SendAsync<FeatureAccessData>(
            HttpMethod.Get, $"/FeatureSvc/{Uri.EscapeDataString(featureCode)}", null, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public Task<GdprRequestData> RequestDataExportAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        return SendAsync<GdprRequestData>(
            HttpMethod.Post, "/UserSvc/data-export", null, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public Task<GdprRequestData> RequestAccountDeletionAsync(string accessToken, string confirmEmail, string? reason = null, CancellationToken cancellationToken = default)
    {
        return SendAsync<GdprRequestData>(
            HttpMethod.Post, "/UserSvc/delete-request",
            new { confirmEmail, reason }, accessToken, cancellationToken);
    }

    /// <summary>
    /// Runs a password-carrying call, refetching the public key and retrying exactly once when
    /// the server reports <c>DECRYPTION_FAILED</c> (stale cached key after a server key rotation).
    /// </summary>
    private async Task<T> SendWithPasswordRetryAsync<T>(Func<string, Task<T>> attempt, CancellationToken cancellationToken)
    {
        var publicKeyPem = await GetPublicKeyAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await attempt(publicKeyPem).ConfigureAwait(false);
        }
        catch (AppManagerException ex) when (ex.Error == AppManagerError.DecryptionFailed)
        {
            logger.LogWarning("AppManager reported DECRYPTION_FAILED — refetching public key and retrying once");
            publicKeyCache.Clear();
            var freshKeyPem = await GetPublicKeyAsync(cancellationToken).ConfigureAwait(false);
            return await attempt(freshKeyPem).ConfigureAwait(false);
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, string? accessToken, CancellationToken cancellationToken)
    {
        var data = await SendCoreAsync<T>(method, url, body, accessToken, cancellationToken).ConfigureAwait(false);
        return data ?? throw new AppManagerException(
            "EMPTY_RESPONSE", $"AppManager returned success without data for {method} {url}");
    }

    private Task SendVoidAsync(HttpMethod method, string url, object? body, string? accessToken, CancellationToken cancellationToken)
    {
        return SendCoreAsync<JsonElement?>(method, url, body, accessToken, cancellationToken);
    }

    private async Task<T?> SendCoreAsync<T>(HttpMethod method, string url, object? body, string? accessToken, CancellationToken cancellationToken)
    {
        if (!options.IsConfigured)
        {
            throw new AppManagerException(
                "NOT_CONFIGURED",
                "AppManager:BaseUrl is not configured — TechieDesk is running in offline single-user mode");
        }

        using var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", options.ApiKey);
        }

        if (!string.IsNullOrEmpty(options.ApiSecret))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Secret", options.ApiSecret);
        }

        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        ApiResponse<T>? parsed = null;
        try
        {
            parsed = await response.Content
                .ReadFromJsonAsync<ApiResponse<T>>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Non-JSON body (e.g. proxy error page) — fall through to the HTTP-status error path.
        }

        if (parsed is { Success: true })
        {
            return parsed.Data;
        }

        var errorCode = parsed?.Error ?? $"HTTP_{(int)response.StatusCode}";
        var message = parsed?.Message
            ?? $"AppManager call {method} {url} failed with HTTP {(int)response.StatusCode}";
        logger.LogWarning("AppManager error {ErrorCode} on {Method} {Url}: {ErrorMessage}",
            errorCode, method, url, message);
        throw new AppManagerException(errorCode, message, (int)response.StatusCode);
    }
}
