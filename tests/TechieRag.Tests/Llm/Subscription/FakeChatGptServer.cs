using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TechieRag.Tests.Llm.Subscription;

/// <summary>
/// A fake OpenAI authorisation server and Codex backend behind one <see cref="HttpMessageHandler"/>,
/// so the ChatGPT subscription sign-in can be driven end to end offline (REQ-RAG-069).
/// </summary>
/// <remarks>
/// <para>It speaks the device-code flow as checked on 2026-09-24: <c>deviceauth/usercode</c> hands out a
/// code, <c>deviceauth/token</c> answers 403 ("not yet") until the test calls <see cref="Authorise"/> or
/// until <see cref="PendingPolls"/> polls have passed, <c>oauth/token</c> exchanges the code or a refresh
/// token, and <c>/responses</c> streams a Responses-API answer to a request carrying a live access token.</para>
/// <para>Every token here is fabricated; no real credential is involved.</para>
/// </remarks>
internal sealed class FakeChatGptServer : HttpMessageHandler
{
    /// <summary>The account id the fake identity token carries.</summary>
    public const string AccountId = "acct-fake-123";

    /// <summary>The user code the fake server hands out.</summary>
    public const string UserCode = "ABCD-1234";

    private readonly HashSet<string> liveAccessTokens = [];
    private int issued;
    private bool authorised;

    /// <summary>Gets or sets how many polls answer "pending" before the sign-in is authorised on its own.</summary>
    public int PendingPolls { get; set; } = 2;

    /// <summary>Gets or sets the Responses-API stream the backend returns.</summary>
    public string ResponseStream { get; set; } = TextStream("Hello ", "from ChatGPT.");

    /// <summary>Gets or sets whether a refresh request is refused.</summary>
    public bool RefuseRefresh { get; set; }

    /// <summary>Gets the path of every request, in order.</summary>
    public ConcurrentQueue<string> Paths { get; } = new();

    /// <summary>Gets every backend request body, in order.</summary>
    public List<string> BackendBodies { get; } = [];

    /// <summary>Gets every backend request, in order, for header checks.</summary>
    public List<HttpRequestMessage> BackendRequests { get; } = [];

    /// <summary>Gets the number of device-code polls received.</summary>
    public int Polls { get; private set; }

    /// <summary>Gets the number of code exchanges received.</summary>
    public int CodeExchanges { get; private set; }

    /// <summary>Gets the number of refreshes received.</summary>
    public int Refreshes { get; private set; }

    /// <summary>Marks the pending sign-in as authorised, as a user approving it in the browser would.</summary>
    public void Authorise() => authorised = true;

    /// <summary>Makes every access token issued so far invalid, as a revocation would.</summary>
    public void RevokeAll() => liveAccessTokens.Clear();

    /// <summary>Registers an access token the backend accepts, for a session restored from a store.</summary>
    /// <param name="token">The token.</param>
    public void Accept(string token) => liveAccessTokens.Add(token);

    /// <summary>Builds an unsigned JWT with the given payload.</summary>
    /// <param name="payload">The claims.</param>
    /// <returns>The token.</returns>
    public static string Jwt(object payload) =>
        $"{Base64Url("{\"alg\":\"none\"}")}.{Base64Url(JsonSerializer.Serialize(payload))}.";

    /// <summary>Builds a Responses-API stream of text deltas followed by <c>response.completed</c>.</summary>
    /// <param name="deltas">The text pieces.</param>
    /// <returns>The server-sent-event body.</returns>
    public static string TextStream(params string[] deltas)
    {
        var builder = new StringBuilder();
        foreach (var delta in deltas)
        {
            Event(builder, new { type = "response.output_text.delta", delta });
        }

        Event(builder, new { type = "response.completed", response = new { usage = new { input_tokens = 12, output_tokens = 5, input_tokens_details = new { cached_tokens = 4 } } } });
        return builder.ToString();
    }

    /// <summary>Builds a stream with one text delta and one function call.</summary>
    /// <returns>The server-sent-event body.</returns>
    public static string ToolCallStream()
    {
        var builder = new StringBuilder();
        Event(builder, new { type = "response.output_text.delta", delta = "Let me check." });
        Event(builder, new { type = "response.output_item.done", item = new { type = "function_call", call_id = "call_1", name = "get_weather", arguments = "{\"city\":\"Paris\"}" } });
        Event(builder, new { type = "response.completed", response = new { usage = new { input_tokens = 20, output_tokens = 9 } } });
        return builder.ToString();
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Paths.Enqueue(path);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

        return path switch
        {
            "/api/accounts/deviceauth/usercode" => Json(new { device_auth_id = "dev-1", user_code = UserCode, interval = "1" }),
            "/api/accounts/deviceauth/token" => Poll(),
            "/oauth/token" => Token(body),
            "/backend-api/codex/responses" => Backend(request, body),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    private HttpResponseMessage Poll()
    {
        Polls++;
        if (!authorised && Polls <= PendingPolls) return new HttpResponseMessage(HttpStatusCode.Forbidden);

        return Json(new { authorization_code = "auth-code-1", code_verifier = "verifier-1", code_challenge = "challenge-1" });
    }

    private HttpResponseMessage Token(string body)
    {
        if (body.Contains("grant_type=authorization_code", StringComparison.Ordinal))
        {
            CodeExchanges++;
            var valid = body.Contains("code=auth-code-1", StringComparison.Ordinal)
                && body.Contains("code_verifier=verifier-1", StringComparison.Ordinal)
                && body.Contains("redirect_uri=https%3A%2F%2Fauth.openai.com%2Fdeviceauth%2Fcallback", StringComparison.Ordinal);
            return valid ? IssueTokens() : new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        Refreshes++;
        var isRefresh = body.Contains("\"grant_type\":\"refresh_token\"", StringComparison.Ordinal);
        return isRefresh && !RefuseRefresh ? IssueTokens() : new HttpResponseMessage(HttpStatusCode.BadRequest);
    }

    private HttpResponseMessage IssueTokens()
    {
        issued++;
        var accessToken = Jwt(new { exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), n = issued });
        lock (liveAccessTokens) liveAccessTokens.Add(accessToken);
        var idToken = Jwt(new Dictionary<string, object> { ["https://api.openai.com/auth"] = new { chatgpt_account_id = AccountId } });

        return Json(new { access_token = accessToken, refresh_token = $"refresh-{issued}", id_token = idToken, expires_in = 3600 });
    }

    private HttpResponseMessage Backend(HttpRequestMessage request, string body)
    {
        var token = request.Headers.Authorization?.Parameter;
        lock (liveAccessTokens)
        {
            BackendBodies.Add(body);
            BackendRequests.Add(request);
            if (token is null || !liveAccessTokens.Contains(token)) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ResponseStream, Encoding.UTF8, "text/event-stream") };
    }

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private static void Event(StringBuilder builder, object value) =>
        builder.Append("data: ").Append(JsonSerializer.Serialize(value)).Append("\n\n");

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
