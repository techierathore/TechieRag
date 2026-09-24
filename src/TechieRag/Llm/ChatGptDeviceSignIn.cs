using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// OpenAI's device-code sign-in and token refresh for a ChatGPT subscription (REQ-RAG-069 / BRD-112).
/// </summary>
/// <remarks>
/// <para><b>The flow</b> (checked 2026-09-24 against OpenAI's open-source Codex client, DECISIONS.md):</para>
/// <list type="number">
/// <item><description><c>POST {issuer}/api/accounts/deviceauth/usercode</c> with the client id returns a
/// device-auth id, the user code and the poll interval.</description></item>
/// <item><description>The host's callback shows <c>{issuer}/codex/device</c> and the code.</description></item>
/// <item><description><c>POST {issuer}/api/accounts/deviceauth/token</c> is polled; 403 or 404 means "not yet";
/// success returns an authorisation code and its PKCE verifier.</description></item>
/// <item><description><c>POST {issuer}/oauth/token</c> exchanges them for identity, access and refresh
/// tokens.</description></item>
/// </list>
/// <para>No token, code or verifier is ever logged or put in an exception message.</para>
/// </remarks>
internal sealed class ChatGptDeviceSignIn
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    private readonly HttpClient httpClient;
    private readonly ChatGptSubscriptionOptions options;

    /// <summary>Creates the sign-in client.</summary>
    /// <param name="httpClient">The client requests go through.</param>
    /// <param name="options">Issuer, client id, timeout and test seams.</param>
    public ChatGptDeviceSignIn(HttpClient httpClient, ChatGptSubscriptionOptions options)
    {
        this.httpClient = httpClient;
        this.options = options;
    }

    /// <summary>Runs the device-code flow to completion.</summary>
    /// <param name="signInCallback">The host's callback that shows the user the page and code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new session.</returns>
    /// <exception cref="SubscriptionSignInException">The vendor refused, or the user did not authorise in time.</exception>
    public async Task<SubscriptionSession> SignInAsync(
        Func<SubscriptionSignInPrompt, CancellationToken, Task> signInCallback,
        CancellationToken cancellationToken)
    {
        var device = await RequestUserCodeAsync(cancellationToken).ConfigureAwait(false);
        var expiresAt = options.Clock() + options.SignInTimeout;

        await signInCallback(new SubscriptionSignInPrompt
        {
            ConnectorName = SubscriptionConnectorRows.ChatGptName,
            VerificationUri = IssuerUri("codex/device"),
            UserCode = device.UserCode,
            ExpiresAt = expiresAt
        }, cancellationToken).ConfigureAwait(false);

        var grant = await PollForAuthorisationAsync(device, expiresAt, cancellationToken).ConfigureAwait(false);
        return await ExchangeCodeAsync(grant, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Exchanges the refresh token for a new access token.</summary>
    /// <param name="session">The session to refresh; must carry a refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refreshed session, keeping the old refresh and identity tokens when none are returned.</returns>
    /// <exception cref="SubscriptionSignInException">The vendor refused the refresh token.</exception>
    public async Task<SubscriptionSession> RefreshAsync(SubscriptionSession session, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = session.RefreshToken ?? string.Empty,
            ["scope"] = "openid profile email"
        };

        using var response = await httpClient.PostAsJsonAsync(IssuerUri("oauth/token"), body, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new SubscriptionSignInException(
                SubscriptionSignInException.CodeSessionRejected,
                $"The ChatGPT token refresh was refused ({(int)response.StatusCode}).");
        }

        var tokens = await ReadTokensAsync(response, cancellationToken).ConfigureAwait(false);
        return BuildSession(tokens, session);
    }

    private async Task<DeviceCode> RequestUserCodeAsync(CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string> { ["client_id"] = options.ClientId };
        using var response = await httpClient.PostAsJsonAsync(IssuerUri("api/accounts/deviceauth/usercode"), body, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new SubscriptionSignInException(
                SubscriptionSignInException.CodeRejected,
                $"The ChatGPT sign-in request was refused ({(int)response.StatusCode}); device-code sign-in may be off in the account's security settings.");
        }

        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = json.RootElement;
        var userCode = ReadString(root, "user_code") ?? ReadString(root, "usercode");
        var deviceAuthId = ReadString(root, "device_auth_id");
        if (string.IsNullOrEmpty(userCode) || string.IsNullOrEmpty(deviceAuthId))
        {
            throw new SubscriptionSignInException(SubscriptionSignInException.CodeRejected, "The ChatGPT sign-in response had no user code.");
        }

        return new DeviceCode(deviceAuthId, userCode, ReadInterval(root));
    }

    private async Task<AuthorisationGrant> PollForAuthorisationAsync(DeviceCode device, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string> { ["device_auth_id"] = device.DeviceAuthId, ["user_code"] = device.UserCode };

        while (options.Clock() < expiresAt)
        {
            using var response = await httpClient.PostAsJsonAsync(IssuerUri("api/accounts/deviceauth/token"), body, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return await ReadGrantAsync(response, cancellationToken).ConfigureAwait(false);
            }

            if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.NotFound))
            {
                throw new SubscriptionSignInException(
                    SubscriptionSignInException.CodeRejected,
                    $"The ChatGPT sign-in was refused while waiting for authorisation ({(int)response.StatusCode}).");
            }

            await options.Delay(device.Interval, cancellationToken).ConfigureAwait(false);
        }

        throw new SubscriptionSignInException(SubscriptionSignInException.CodeExpired, "The ChatGPT sign-in code expired before the user authorised it.");
    }

    private async Task<AuthorisationGrant> ReadGrantAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var code = ReadString(json.RootElement, "authorization_code");
        var verifier = ReadString(json.RootElement, "code_verifier");
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(verifier))
        {
            throw new SubscriptionSignInException(SubscriptionSignInException.CodeRejected, "The ChatGPT authorisation carried no code.");
        }

        return new AuthorisationGrant(code, verifier);
    }

    private async Task<SubscriptionSession> ExchangeCodeAsync(AuthorisationGrant grant, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = grant.Code,
            ["redirect_uri"] = IssuerUri("deviceauth/callback").ToString(),
            ["client_id"] = options.ClientId,
            ["code_verifier"] = grant.CodeVerifier
        });

        using var response = await httpClient.PostAsync(IssuerUri("oauth/token"), form, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new SubscriptionSignInException(
                SubscriptionSignInException.CodeRejected,
                $"The ChatGPT code exchange was refused ({(int)response.StatusCode}).");
        }

        var tokens = await ReadTokensAsync(response, cancellationToken).ConfigureAwait(false);
        return BuildSession(tokens, previous: null);
    }

    private SubscriptionSession BuildSession(TokenSet tokens, SubscriptionSession? previous)
    {
        var idToken = tokens.IdToken ?? previous?.IdToken;
        var expiresAt = tokens.ExpiresIn is { } seconds
            ? options.Clock().AddSeconds(seconds)
            : JwtClaimReader.ReadExpiry(tokens.AccessToken);

        return new SubscriptionSession
        {
            ConnectorName = SubscriptionConnectorRows.ChatGptName,
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken ?? previous?.RefreshToken,
            IdToken = idToken,
            AccountId = JwtClaimReader.ReadChatGptAccountId(idToken)
                ?? JwtClaimReader.ReadChatGptAccountId(tokens.AccessToken)
                ?? previous?.AccountId,
            ExpiresAt = expiresAt
        };
    }

    private static async Task<TokenSet> ReadTokensAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = json.RootElement;
        var accessToken = ReadString(root, "access_token");
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new SubscriptionSignInException(SubscriptionSignInException.CodeRejected, "The ChatGPT token response carried no access token.");
        }

        long? expiresIn = root.TryGetProperty("expires_in", out var value) && value.TryGetInt64(out var seconds) ? seconds : null;
        return new TokenSet(accessToken, ReadString(root, "refresh_token"), ReadString(root, "id_token"), expiresIn);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static TimeSpan ReadInterval(JsonElement root)
    {
        if (!root.TryGetProperty("interval", out var value)) return DefaultPollInterval;

        // The Codex client accepts the interval as a number or as a numeric string.
        var seconds = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => 0
        };

        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : DefaultPollInterval;
    }

    private Uri IssuerUri(string relativePath) => new(new Uri(options.Issuer.ToString().TrimEnd('/') + "/"), relativePath);

    private sealed record DeviceCode(string DeviceAuthId, string UserCode, TimeSpan Interval);

    private sealed record AuthorisationGrant(string Code, string CodeVerifier);

    private sealed record TokenSet(string AccessToken, string? RefreshToken, string? IdToken, long? ExpiresIn);
}
