using System.Net;
using System.Net.Sockets;
using System.Text;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// A failed LLM call carries the provider's error text (REQ-RAG-134, Lekhak feedback TR-RAG-007): the status
/// code, the trimmed response body and the provider's own message, in an <see cref="LlmHttpException"/> that is
/// still an <see cref="HttpRequestException"/>.
/// </summary>
/// <remarks>
/// The end-to-end tests run a loopback HTTP server that answers every request with one status and body, so the
/// public calls are proven to read what a real service sends back.
/// </remarks>
public sealed class ProviderErrorTextTests
{
    /// <summary>
    /// Lekhak's repro: an OpenAI-compatible endpoint answers <c>POST /chat/completions</c> with
    /// <c>404 {"error":{"message":"Specified model not found: wrong-model"}}</c>. <c>ChatAsync</c> throws an
    /// <see cref="LlmHttpException"/> whose message keeps the standard status line and adds the provider's message,
    /// and which exposes the status code, the body and the message.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-134 FailedChatCarriesProviderMessage")]
    public async Task FailedChatCarriesProviderMessage()
    {
        const string body = """{"error":{"message":"Specified model not found: wrong-model","type":"invalid_request_error"}}""";
        using var server = new StatusServer(HttpStatusCode.NotFound, body);

        var provider = LlmProviderFactory.CreateForModel("openai/wrong-model", "key", null, 2048, null, endpoint: server.BaseUrl + "/v1");
        var exception = await Assert.ThrowsAsync<LlmHttpException>(
            () => provider.ChatAsync([ChatMessage.User("Say OK.")], new LlmCompletionOptions { MaxTokens = 5 }));

        Assert.IsAssignableFrom<HttpRequestException>(exception);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("Specified model not found: wrong-model", exception.ProviderMessage);
        Assert.Equal(body, exception.ResponseBody);
        Assert.StartsWith("Response status code does not indicate success: 404 (Not Found).", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Specified model not found: wrong-model", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A streamed chat to Ollama that answers <c>404 {"error":"model 'llama9' not found"}</c> throws the same
    /// exception before the first event, with Ollama's text-shaped error read as the provider message.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-134 FailedStreamCarriesProviderMessage")]
    public async Task FailedStreamCarriesProviderMessage()
    {
        using var server = new StatusServer(HttpStatusCode.NotFound, """{"error":"model 'llama9' not found"}""");

        var provider = LlmProviderFactory.CreateForModel("ollama/llama9", null, endpoint: server.BaseUrl);
        var exception = await Assert.ThrowsAsync<LlmHttpException>(async () =>
        {
            await foreach (var _ in provider.ChatStreamEventsAsync([ChatMessage.User("hi")]))
            {
            }
        });

        Assert.Equal("model 'llama9' not found", exception.ProviderMessage);
        Assert.Contains("model 'llama9' not found", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A 429 with <c>Retry-After</c> still throws <see cref="LlmRateLimitException"/> with its delay, and now also
    /// carries the provider's message; a failed model listing carries it too.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-134 RateLimitAndListingKeepProviderMessage")]
    public async Task RateLimitAndListingKeepProviderMessage()
    {
        using (var limited = new StatusServer(HttpStatusCode.TooManyRequests, """{"error":{"message":"Quota exceeded for this key."}}""", retryAfterSeconds: 7))
        {
            var provider = LlmProviderFactory.CreateForModel("openai/m", "key", endpoint: limited.BaseUrl + "/v1");
            var exception = await Assert.ThrowsAsync<LlmRateLimitException>(() => provider.ChatAsync([ChatMessage.User("hi")]));

            Assert.Equal(TimeSpan.FromSeconds(7), exception.RetryAfter);
            Assert.Equal("Quota exceeded for this key.", exception.ProviderMessage);
            Assert.Contains("Quota exceeded for this key.", exception.Message, StringComparison.Ordinal);
        }

        using var refused = new StatusServer(HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid API key."}}""");
        var listing = await Assert.ThrowsAsync<LlmHttpException>(
            () => LlmProviderFactory.ListModelsAsync(ModelRouter.Require("openai/m"), "key", refused.BaseUrl + "/v1"));
        Assert.Equal(HttpStatusCode.Unauthorized, listing.StatusCode);
        Assert.Equal("Invalid API key.", listing.ProviderMessage);
    }

    /// <summary>
    /// The provider message is read from every common error shape (Anthropic's and Gemini's nested
    /// <c>error.message</c>, a top-level <c>message</c> or <c>detail</c>, an array holding an error); a body that is
    /// not JSON gives none; an HTML error page adds nothing to the message but a plain-text body is quoted;
    /// a long body is cut to <see cref="LlmHttpException.MaxBodyLength"/>.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-134 ProviderMessageShapesAreRead")]
    public async Task ProviderMessageShapesAreRead()
    {
        Assert.Equal("model: nope", LlmHttpGuard.ExtractProviderMessage("""{"type":"error","error":{"type":"not_found_error","message":"model: nope"}}"""));
        Assert.Equal("Not found", LlmHttpGuard.ExtractProviderMessage("""[{"error":{"code":404,"message":"Not found","status":"NOT_FOUND"}}]"""));
        Assert.Equal("bad input", LlmHttpGuard.ExtractProviderMessage("""{"message":"bad input"}"""));
        Assert.Equal("Not Found", LlmHttpGuard.ExtractProviderMessage("""{"detail":"Not Found"}"""));
        Assert.Null(LlmHttpGuard.ExtractProviderMessage("upstream timed out"));
        Assert.Null(LlmHttpGuard.ExtractProviderMessage("""{"ok":false}"""));
        Assert.Null(LlmHttpGuard.ExtractProviderMessage(null));

        using var html = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html><body>502 Bad Gateway</body></html>") };
        var htmlError = await Assert.ThrowsAsync<LlmHttpException>(() => LlmHttpGuard.EnsureSuccessAsync(html, CancellationToken.None));
        Assert.Equal("Response status code does not indicate success: 502 (Bad Gateway).", htmlError.Message);
        Assert.Null(htmlError.ProviderMessage);

        using var text = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("upstream   timed\nout") };
        var textError = await Assert.ThrowsAsync<LlmHttpException>(() => LlmHttpGuard.EnsureSuccessAsync(text, CancellationToken.None));
        Assert.EndsWith("Response body: upstream timed out", textError.Message, StringComparison.Ordinal);

        using var huge = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(new string('x', 10_000)) };
        var hugeError = await Assert.ThrowsAsync<LlmHttpException>(() => LlmHttpGuard.EnsureSuccessAsync(huge, CancellationToken.None));
        Assert.Equal(LlmHttpException.MaxBodyLength, hugeError.ResponseBody!.Length);
        Assert.True(hugeError.Message.Length < 700);

        using var ok = new HttpResponseMessage(HttpStatusCode.OK);
        await LlmHttpGuard.EnsureSuccessAsync(ok, CancellationToken.None);
    }

    /// <summary>A loopback HTTP server that answers every request with one status code and body.</summary>
    private sealed class StatusServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly HttpStatusCode status;
        private readonly string body;
        private readonly int? retryAfterSeconds;

        /// <summary>Starts the server on a free loopback port.</summary>
        /// <param name="status">The status every request gets.</param>
        /// <param name="body">The JSON body every request gets.</param>
        /// <param name="retryAfterSeconds">A <c>Retry-After</c> header value, or null for none.</param>
        public StatusServer(HttpStatusCode status, string body, int? retryAfterSeconds = null)
        {
            this.status = status;
            this.body = body;
            this.retryAfterSeconds = retryAfterSeconds;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            BaseUrl = $"http://127.0.0.1:{port}";
            listener.Prefixes.Add(BaseUrl + "/");
            listener.Start();
            _ = Task.Run(ServeAsync);
        }

        /// <summary>Gets the server's base URL, with no trailing slash.</summary>
        public string BaseUrl { get; }

        /// <inheritdoc/>
        public void Dispose() => listener.Close();

        private async Task ServeAsync()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    await reader.ReadToEndAsync();
                }

                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = (int)status;
                context.Response.ContentType = "application/json";
                if (retryAfterSeconds is not null)
                    context.Response.Headers["Retry-After"] = retryAfterSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
    }
}
