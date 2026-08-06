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

    /// <summary>
    /// Request header carrying this installation's identity on licence validation (REQ-FN-051).
    /// </summary>
    /// <remarks>
    /// Named as a constant so the one test that asserts the header and the one call site that sends
    /// it cannot drift apart, and so a future AppManager contract change has a single place to land.
    /// </remarks>
    public const string InstallIdentityHeaderName = "X-Install-Id";

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
        // REQ-UI-007: without the v1.4 aApplicationId parameter AppManager cannot resolve which
        // application the caller is authenticating against, so it returns an EMPTY applicationRole
        // and cannot tell which application the licence belongs to. Same convention as
        // ValidateLicenseAsync. (The role itself no longer gates anything — REQ-FN-041.)
        var url = "/AuthSvc/login";
        if (options.ApplicationId.HasValue)
        {
            url += $"?aApplicationId={options.ApplicationId.Value}";
        }

        return SendWithPasswordRetryAsync(
            keyPem => SendAsync<AuthResponseData>(HttpMethod.Post, url, new
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
    public Task<LicenseValidationData> ValidateLicenseAsync(
        string accessToken, string? installId = null, CancellationToken cancellationToken = default)
    {
        var url = "/LicenseSvc/validate";
        if (options.ApplicationId.HasValue)
        {
            url += $"?aApplicationId={options.ApplicationId.Value}";
        }

        // REQ-FN-051 clause 2. A HEADER, not a body: the guide specifies this endpoint as taking no
        // request body, so introducing one is the change that could break a contract nobody here can
        // test against a live server. An unrecognised header is discarded by any HTTP stack, and
        // when installId is null the request is byte-for-byte what it was before this requirement.
        var headers = installId is null
            ? null
            : new[] { new KeyValuePair<string, string>(InstallIdentityHeaderName, installId) };

        return SendAsync<LicenseValidationData>(
            HttpMethod.Post, url, null, accessToken, cancellationToken, headers);
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupportIssueData>> ListIssuesAsync(string accessToken, string? status = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (options.ApplicationId.HasValue)
        {
            query.Add($"aApplicationId={options.ApplicationId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"aStatus={Uri.EscapeDataString(status)}");
        }

        var url = query.Count == 0 ? "/IssueSvc" : $"/IssueSvc?{string.Join('&', query)}";

        // Deliberately NOT SendAsync: that treats a missing data payload as EMPTY_RESPONSE, and
        // "this user has raised no issues" is a legitimate answer rather than a protocol failure.
        // A caller that cannot reach the service still gets an AppManagerException from the core.
        var data = await SendCoreAsync<List<SupportIssueData>>(
            HttpMethod.Get, url, null, accessToken, cancellationToken).ConfigureAwait(false);

        return data ?? new List<SupportIssueData>();
    }

    /// <inheritdoc />
    public Task<SupportIssueData> GetIssueAsync(string accessToken, int issueId, CancellationToken cancellationToken = default)
    {
        return SendAsync<SupportIssueData>(
            HttpMethod.Get, $"/IssueSvc/{issueId}", null, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CreatedIssueData> CreateIssueAsync(string accessToken, CreateIssueRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<CreatedIssueData>(
            HttpMethod.Post, "/IssueSvc", request, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public Task AddIssueCommentAsync(string accessToken, int issueId, string comment, CancellationToken cancellationToken = default)
    {
        return SendVoidAsync(
            HttpMethod.Post, $"/IssueSvc/{issueId}/comments",
            new { comment }, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public Task CloseIssueAsync(string accessToken, int issueId, CancellationToken cancellationToken = default)
    {
        // The endpoint takes no body; sending one would be a change to the wire contract.
        return SendVoidAsync(
            HttpMethod.Post, $"/IssueSvc/{issueId}/close", null, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LicenseTypeData>> GetLicenseTypesAsync(string? currency = null, CancellationToken cancellationToken = default)
    {
        var url = BuildUrl("/LicenseSvc/types", currency is { Length: > 0 }
            ? new[] { $"aCurrency={Uri.EscapeDataString(currency)}" }
            : Array.Empty<string>());

        // "This application sells nothing yet" is a legitimate answer, not a protocol failure, so
        // an absent data payload becomes an empty catalogue rather than EMPTY_RESPONSE.
        var data = await SendCoreAsync<List<LicenseTypeData>>(
            HttpMethod.Get, url, null, null, cancellationToken).ConfigureAwait(false);

        return data ?? new List<LicenseTypeData>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserLicenseData>> GetLicensesAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var data = await SendCoreAsync<List<UserLicenseData>>(
            HttpMethod.Get, BuildUrl("/LicenseSvc"), null, accessToken, cancellationToken).ConfigureAwait(false);

        return data ?? new List<UserLicenseData>();
    }

    /// <inheritdoc />
    public Task DeactivateDeviceAsync(string accessToken, int licenseId, int deviceId, CancellationToken cancellationToken = default)
    {
        return SendVoidAsync(
            HttpMethod.Delete, $"/LicenseSvc/{licenseId}/devices/{deviceId}",
            null, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubscriptionData>> GetSubscriptionsAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        // Having no subscription is the normal state for a perpetual-licence or free user, so an
        // absent payload is an empty list — never an error the billing screen has to explain away.
        var data = await SendCoreAsync<List<SubscriptionData>>(
            HttpMethod.Get, BuildUrl("/PaymentSvc/subscriptions"), null, accessToken, cancellationToken).ConfigureAwait(false);

        return data ?? new List<SubscriptionData>();
    }

    /// <inheritdoc />
    public Task CancelSubscriptionAsync(string accessToken, int subscriptionId, bool cancelImmediately = false, string? reason = null, CancellationToken cancellationToken = default)
    {
        return SendVoidAsync(
            HttpMethod.Post, $"/PaymentSvc/subscriptions/{subscriptionId}/cancel",
            new { cancelImmediately, reason }, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResultData<TransactionData>> GetTransactionsAsync(string accessToken, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var url = BuildUrl("/PaymentSvc/transactions", new[] { $"aPage={page}", $"aPageSize={pageSize}" });
        var data = await SendCoreAsync<PagedResultData<TransactionData>>(
            HttpMethod.Get, url, null, accessToken, cancellationToken).ConfigureAwait(false);

        return data ?? new PagedResultData<TransactionData> { Page = page, PageSize = pageSize };
    }

    /// <inheritdoc />
    public async Task<PagedResultData<InvoiceData>> GetInvoicesAsync(string accessToken, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var url = BuildUrl("/PaymentSvc/invoices", new[] { $"aPage={page}", $"aPageSize={pageSize}" });
        var data = await SendCoreAsync<PagedResultData<InvoiceData>>(
            HttpMethod.Get, url, null, accessToken, cancellationToken).ConfigureAwait(false);

        return data ?? new PagedResultData<InvoiceData> { Page = page, PageSize = pageSize };
    }

    /// <inheritdoc />
    public async Task<InvoiceDownloadData> DownloadInvoiceAsync(string accessToken, int invoiceId, CancellationToken cancellationToken = default)
    {
        var url = $"/PaymentSvc/invoices/{invoiceId}/download";
        using var response = await SendRawAsync(
            HttpMethod.Get, url, accessToken, cancellationToken).ConfigureAwait(false);

        var mediaType = response.Content.Headers.ContentType?.MediaType;

        // The endpoint answers with PDF bytes on success and the standard JSON error envelope on
        // failure, so the content type — not the status code alone — decides which it is.
        if (!response.IsSuccessStatusCode || !string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            await ThrowFromErrorBodyAsync(response, HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (content.Length == 0)
        {
            throw new AppManagerException(
                "PDF_GENERATION_FAILED",
                $"AppManager returned an empty PDF for invoice {invoiceId}",
                (int)response.StatusCode);
        }

        return new InvoiceDownloadData
        {
            FileName = SanitizeFileName(
                response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName)
                ?? $"invoice-{invoiceId}.pdf",
            ContentType = mediaType ?? "application/pdf",
            Content = content
        };
    }

    /// <inheritdoc />
    public Task<PromoCodeData> ValidatePromoCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        // Anonymous by contract — the ApplicationId travels in the API key headers, which
        // SendCoreAsync attaches to every call.
        return SendAsync<PromoCodeData>(
            HttpMethod.Post, "/PaymentSvc/promo-codes/validate", new { code }, null, cancellationToken);
    }

    /// <summary>
    /// Appends the configured v1.4 <c>aApplicationId</c> parameter (when an explicit ApplicationId
    /// is set) plus any extra query parts to a path.
    /// </summary>
    private string BuildUrl(string path, IReadOnlyList<string>? extraQuery = null)
    {
        var query = new List<string>();
        if (options.ApplicationId.HasValue)
        {
            query.Add($"aApplicationId={options.ApplicationId.Value}");
        }

        if (extraQuery is { Count: > 0 })
        {
            query.AddRange(extraQuery);
        }

        return query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
    }

    /// <summary>
    /// Strips any directory component and quoting from a server-supplied Content-Disposition file
    /// name so it can never escape the folder the caller chose to write into.
    /// </summary>
    private static string? SanitizeFileName(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim().Trim('"');
        var leaf = trimmed
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()?
            .Split('\\', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or "..")
        {
            return null;
        }

        var cleaned = new string(leaf
            .Where(character => !Path.GetInvalidFileNameChars().Contains(character))
            .ToArray());

        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    /// <summary>
    /// Issues a request the same way <see cref="SendCoreAsync{T}"/> does — offline guard, API key
    /// headers, bearer token — but hands back the raw response so a binary body can be read.
    /// </summary>
    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, string? accessToken, CancellationToken cancellationToken)
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

        return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the standard error envelope off a failed response and throws the matching
    /// <see cref="AppManagerException"/>, falling back to the HTTP status when the body is not the
    /// documented JSON shape.
    /// </summary>
    private async Task ThrowFromErrorBodyAsync(HttpResponseMessage response, HttpMethod method, string url, CancellationToken cancellationToken)
    {
        ApiResponse<JsonElement?>? parsed = null;
        try
        {
            parsed = await response.Content
                .ReadFromJsonAsync<ApiResponse<JsonElement?>>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // Non-JSON body (a proxy error page, or an unexpected binary) — fall through to status.
        }

        var errorCode = parsed?.Error ?? $"HTTP_{(int)response.StatusCode}";
        var message = parsed?.Message
            ?? $"AppManager call {method} {url} failed with HTTP {(int)response.StatusCode}";
        logger.LogWarning("AppManager error {ErrorCode} on {Method} {Url}: {ErrorMessage}",
            errorCode, method, url, message);
        throw new AppManagerException(errorCode, message, (int)response.StatusCode);
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

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string url,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken,
        IReadOnlyList<KeyValuePair<string, string>>? extraHeaders = null)
    {
        var data = await SendCoreAsync<T>(method, url, body, accessToken, cancellationToken, extraHeaders)
            .ConfigureAwait(false);
        return data ?? throw new AppManagerException(
            "EMPTY_RESPONSE", $"AppManager returned success without data for {method} {url}");
    }

    private Task SendVoidAsync(HttpMethod method, string url, object? body, string? accessToken, CancellationToken cancellationToken)
    {
        return SendCoreAsync<JsonElement?>(method, url, body, accessToken, cancellationToken);
    }

    private async Task<T?> SendCoreAsync<T>(
        HttpMethod method,
        string url,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken,
        IReadOnlyList<KeyValuePair<string, string>>? extraHeaders = null)
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

        if (extraHeaders is not null)
        {
            foreach (var header in extraHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
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
