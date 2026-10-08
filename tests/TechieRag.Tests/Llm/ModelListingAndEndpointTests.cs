using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Listing a connector's models (REQ-RAG-126, Lekhak TR-RAG-004) and creating a provider from a model
/// name at another endpoint (REQ-RAG-127, Lekhak TR-RAG-005).
/// </summary>
/// <remarks>
/// The wire-shape tests use a stub handler; the end-to-end tests run a loopback HTTP server, so the
/// public calls are proven to reach the endpoint they were given.
/// </remarks>
public sealed class ModelListingAndEndpointTests
{
    /// <summary>
    /// LM Studio is asked <c>GET /v1/models</c> with no key, and the <c>data[].id</c> values come back
    /// distinct and sorted.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-126 LmStudioModelsAreListed")]
    public async Task LmStudioModelsAreListed()
    {
        var handler = new RecordingHandler("""{"object":"list","data":[{"id":"qwen3-8b"},{"id":"gemma-3"},{"id":"qwen3-8b"}]}""");

        var ids = await LlmModelLister.ListAsync(LlmConnectorCatalog.Require("lmstudio"), null, handler, CancellationToken.None);

        Assert.Equal(["gemma-3", "qwen3-8b"], ids);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("http://localhost:1234/v1/models"), request.Uri);
        Assert.Null(request.Authorization);
    }

    /// <summary>Ollama is asked <c>GET /api/tags</c> and its <c>models[].name</c> values come back.</summary>
    [Fact(DisplayName = "REQ-RAG-126 OllamaModelsAreListed")]
    public async Task OllamaModelsAreListed()
    {
        var handler = new RecordingHandler("""{"models":[{"name":"llama3.2:latest","size":2019393189},{"name":"mistral:7b"}]}""");

        var ids = await LlmModelLister.ListAsync(LlmConnectorCatalog.Require("ollama"), null, handler, CancellationToken.None);

        Assert.Equal(["llama3.2:latest", "mistral:7b"], ids);
        Assert.Equal(new Uri("http://localhost:11434/api/tags"), Assert.Single(handler.Requests).Uri);
    }

    /// <summary>
    /// An OpenAI-compatible connector is asked <c>{endpoint}/models</c>, keeping the endpoint's base path,
    /// with the key as a bearer token.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-126 OpenAICompatibleModelsUseBasePathAndKey")]
    public async Task OpenAICompatibleModelsUseBasePathAndKey()
    {
        var handler = new RecordingHandler("""{"data":[{"id":"llama-3.3-70b-versatile"}]}""");

        var ids = await LlmModelLister.ListAsync(LlmConnectorCatalog.Require("groq"), "test-key", handler, CancellationToken.None);

        Assert.Equal(["llama-3.3-70b-versatile"], ids);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("https://api.groq.com/openai/v1/models"), request.Uri);
        Assert.Equal("Bearer test-key", request.Authorization);
    }

    /// <summary>
    /// A connector that needs a key is refused without one before any request; a connector with no listing
    /// call says so; a refusing server throws rather than returning an empty list, and the key is not in the message.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-126 ListingRefusalsAreExplicit")]
    public async Task ListingRefusalsAreExplicit()
    {
        var handler = new RecordingHandler("{}");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => LlmModelLister.ListAsync(LlmConnectorCatalog.Require("openai"), null, handler, CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => LlmModelLister.ListAsync(LlmConnectorCatalog.Require("anthropic"), "test-key", handler, CancellationToken.None));
        Assert.Empty(handler.Requests);

        var failing = new RecordingHandler("""{"error":"down"}""", HttpStatusCode.InternalServerError);
        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => LlmModelLister.ListAsync(LlmConnectorCatalog.Require("openai"), "secret-key-123", failing, CancellationToken.None));
        Assert.DoesNotContain("secret-key-123", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The public listing call with an endpoint reaches that endpoint: an LM Studio on another host (here a
    /// loopback server) answers, and a malformed endpoint is refused before any request.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-126 PublicListingReachesGivenEndpoint")]
    public async Task PublicListingReachesGivenEndpoint()
    {
        using var server = new LoopbackServer("""{"data":[{"id":"phi-4"}]}""");

        var ids = await LlmProviderFactory.ListModelsAsync(ModelRouter.Require("lmstudio/any"), null, server.BaseUrl);

        Assert.Equal(["phi-4"], ids);
        Assert.Equal("GET /v1/models", Assert.Single(server.Requests));
        await Assert.ThrowsAsync<ArgumentException>(
            () => LlmProviderFactory.ListModelsAsync(LlmConnectorCatalog.Require("lmstudio"), null, "not a url"));
    }

    /// <summary>
    /// <c>CreateForModel("lmstudio/…", null, endpoint: …)</c> sends its chat request to that endpoint, with no
    /// rebuilt route; without an endpoint the connector's default stays.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-127 CreateForModelSendsToGivenEndpoint")]
    public async Task CreateForModelSendsToGivenEndpoint()
    {
        using var server = new LoopbackServer(
            """{"id":"x","object":"chat.completion","model":"qwen3","choices":[{"index":0,"message":{"role":"assistant","content":"Hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}""");

        var llm = LlmProviderFactory.CreateForModel("lmstudio/qwen3", null, endpoint: server.BaseUrl);
        var reply = await llm.ChatAsync([ChatMessage.User("hi")]);

        Assert.Equal("Hello", reply.Content);
        Assert.Equal("POST /v1/chat/completions", Assert.Single(server.Requests));
        Assert.Equal("http://localhost:1234", ModelRouter.Require("lmstudio/qwen3").Endpoint);
    }

    /// <summary>
    /// The endpoint replaces the connector's for Ollama too, a malformed endpoint is refused, and the 1.1.2
    /// five-argument call still binds and works.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-127 EndpointOverrideIsValidatedAndOldCallStillWorks")]
    public void EndpointOverrideIsValidatedAndOldCallStillWorks()
    {
        var route = LlmProviderFactory.WithEndpoint(ModelRouter.Require("ollama/llama3.2"), "http://192.168.1.20:11434");
        Assert.Equal("http://192.168.1.20:11434", route.Endpoint);
        Assert.Equal("llama3.2", route.ModelId);

        Assert.Throws<ArgumentException>(() => LlmProviderFactory.CreateForModel("lmstudio/qwen3", null, endpoint: "ftp://host"));
        Assert.Throws<ArgumentException>(() => LlmProviderFactory.CreateForModel("lmstudio/qwen3", null, endpoint: "/relative"));

        var oldShape = LlmProviderFactory.CreateForModel("lmstudio/qwen3", null, null, 2048, null);
        Assert.IsType<LmStudioLlmProvider>(oldShape);
    }

    /// <summary>A stub handler that records each request's method, URI and authorization header.</summary>
    private sealed class RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        /// <summary>Gets the requests seen.</summary>
        public List<(HttpMethod Method, Uri? Uri, string? Authorization)> Requests { get; } = [];

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri, request.Headers.Authorization?.ToString()));
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>A loopback HTTP server that answers every request with one JSON body and records "METHOD /path".</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly string body;

        /// <summary>Starts the server on a free loopback port.</summary>
        /// <param name="body">The JSON every request gets.</param>
        public LoopbackServer(string body)
        {
            this.body = body;
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

        /// <summary>Gets the requests seen, as "METHOD /path".</summary>
        public ConcurrentQueue<string> Requests { get; } = new();

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

                Requests.Enqueue($"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}");
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
    }
}
