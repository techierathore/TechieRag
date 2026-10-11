using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// The generic <c>openai-compatible</c> and <c>anthropic-compatible</c> routes (REQ-RAG-130), the Venice
/// connector (REQ-RAG-131), the catalog's self-description (REQ-RAG-132) and the Anthropic provider keeping
/// its endpoint's path (REQ-RAG-133). Owner report of 2026-10-11 from Lekhak's provider page.
/// </summary>
/// <remarks>
/// The end-to-end tests run a loopback HTTP server under a base path, so the public calls are proven to send
/// the right wire format to the endpoint they were given, path included.
/// </remarks>
public sealed class GenericRoutesAndVeniceTests
{
    private const string ChatCompletionReply =
        """{"id":"x","object":"chat.completion","model":"glm-4.6","choices":[{"index":0,"message":{"role":"assistant","content":"Hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}""";

    private const string AnthropicReply =
        """{"id":"msg_1","type":"message","role":"assistant","model":"glm-4.6","content":[{"type":"text","text":"Hello"}],"stop_reason":"end_turn","usage":{"input_tokens":3,"output_tokens":1}}""";

    /// <summary>
    /// <c>openai-compatible/glm-4.6@{endpoint}</c> posts chat/completions to that endpoint, keeping its path,
    /// with the model id alone and the key as a bearer token.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-130 OpenAICompatibleRouteSendsToSuffixEndpoint")]
    public async Task OpenAICompatibleRouteSendsToSuffixEndpoint()
    {
        using var server = new LoopbackServer(ChatCompletionReply);

        var llm = LlmProviderFactory.CreateForModel($"openai-compatible/glm-4.6@{server.BaseUrl}/api/paas/v4", "zai-key");
        var reply = await llm.ChatAsync([ChatMessage.User("hi")]);

        Assert.Equal("Hello", reply.Content);
        var request = Assert.Single(server.Requests);
        Assert.Equal("POST /api/paas/v4/chat/completions", request.Line);
        Assert.Equal("Bearer zai-key", request.Authorization);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("glm-4.6", body.RootElement.GetProperty("model").GetString());
    }

    /// <summary>
    /// <c>anthropic-compatible/glm-4.6</c> with the app's Base URL posts the Anthropic Messages format to
    /// <c>{endpoint}/v1/messages</c> with <c>x-api-key</c>, as GLM's Anthropic-compatible endpoint expects.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-130 AnthropicCompatibleRouteUsesBaseUrlAndMessagesFormat")]
    public async Task AnthropicCompatibleRouteUsesBaseUrlAndMessagesFormat()
    {
        using var server = new LoopbackServer(AnthropicReply);

        var llm = LlmProviderFactory.CreateForModel("anthropic-compatible/glm-4.6", "zai-key", endpoint: server.BaseUrl + "/api/anthropic");
        var reply = await llm.ChatAsync([ChatMessage.System("Be brief."), ChatMessage.User("hi")]);

        Assert.IsType<AnthropicLlmProvider>(llm);
        Assert.Equal("Hello", reply.Content);
        var request = Assert.Single(server.Requests);
        Assert.Equal("POST /api/anthropic/v1/messages", request.Line);
        Assert.Equal("zai-key", request.ApiKey);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("glm-4.6", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("Be brief.", body.RootElement.GetProperty("system").GetString());
        Assert.True(body.RootElement.TryGetProperty("max_tokens", out _));
    }

    /// <summary>
    /// Without an endpoint either generic route is refused, by the factory and by the builder, with a message
    /// naming both ways to give one; without a key either is refused; an endpoint on the route wins over the app's; a model id holding an
    /// <c>@</c> of its own stays whole; a malformed suffix is refused.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-130 GenericRouteWithoutEndpointIsRefused")]
    public void GenericRouteWithoutEndpointIsRefused()
    {
        var openAi = Assert.Throws<InvalidOperationException>(() => LlmProviderFactory.CreateForModel("openai-compatible/glm-4.6", "k"));
        Assert.Contains("Base URL", openAi.Message, StringComparison.Ordinal);
        Assert.Contains("openai-compatible/glm-4.6@https://", openAi.Message, StringComparison.Ordinal);

        var anthropic = Assert.Throws<InvalidOperationException>(() => LlmProviderFactory.CreateForModel("anthropic-compatible/glm-4.6", "k"));
        Assert.Contains("anthropic-compatible/glm-4.6@https://", anthropic.Message, StringComparison.Ordinal);

        // A key is required for both, as every hosted OpenAI-compatible vendor needs one.
        foreach (var route in new[] { "openai-compatible/glm-4.6@https://api.z.ai/api/paas/v4", "anthropic-compatible/glm-4.6@https://api.z.ai/api/anthropic" })
        {
            var noKey = Assert.Throws<InvalidOperationException>(() => LlmProviderFactory.CreateForModel(route, null));
            Assert.Contains("API key is required", noKey.Message, StringComparison.Ordinal);
        }

        var builder = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new TestDoubles.FakeEmbeddingProvider())
            .UseSqliteVec(Path.Combine(Path.GetTempPath(), $"trgeneric-{Guid.NewGuid():N}.db"))
            .UseLlmForModel("anthropic-compatible/glm-4.6", "k");
        Assert.Throws<InvalidOperationException>(() => builder.CreateLlmProvider());

        var routed = LlmProviderFactory.WithEndpoint(ModelRouter.Require("openai-compatible/m@https://a.example/v1"), "https://b.example/v1");
        Assert.Equal("https://a.example/v1", routed.Endpoint);
        Assert.Equal("m", routed.ModelId);
        Assert.Equal("claude-3@20240229", ModelRouter.Require("anthropic/claude-3@20240229").ModelId);
        Assert.Throws<ArgumentException>(() => ModelRouter.Resolve("openai-compatible/m@https://"));
    }

    /// <summary>
    /// The builder takes the app's Base URL for a generic route and builds the Anthropic provider at that
    /// endpoint, path kept.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-130 BuilderTakesBaseUrlForGenericRoute")]
    public void BuilderTakesBaseUrlForGenericRoute()
    {
        var llm = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new TestDoubles.FakeEmbeddingProvider())
            .UseSqliteVec(Path.Combine(Path.GetTempPath(), $"trgeneric-{Guid.NewGuid():N}.db"))
            .UseLlmForModel("anthropic-compatible/glm-4.6", "k", "https://api.z.ai/api/anthropic")
            .CreateLlmProvider();

        var anthropic = Assert.IsType<AnthropicLlmProvider>(llm);
        Assert.Equal(new Uri("https://api.z.ai/api/anthropic/v1/messages"), anthropic.EffectiveMessagesUri);
    }

    /// <summary>
    /// An Anthropic-compatible service is asked <c>GET {endpoint}/v1/models</c> with <c>x-api-key</c>; an
    /// OpenAI-compatible one <c>GET {endpoint}/models</c>; a generic route with no endpoint is refused before any request.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-130 GenericRoutesListModels")]
    public async Task GenericRoutesListModels()
    {
        using var server = new LoopbackServer("""{"data":[{"id":"glm-4.6"},{"id":"glm-4.5-air"}]}""");

        var anthropicIds = await LlmProviderFactory.ListModelsAsync(ModelRouter.Require("anthropic-compatible/x"), "zai-key", server.BaseUrl + "/api/anthropic");
        var openAiIds = await LlmProviderFactory.ListModelsAsync(ModelRouter.Require($"openai-compatible/x@{server.BaseUrl}/api/paas/v4"), "zai-key");

        Assert.Equal(["glm-4.5-air", "glm-4.6"], anthropicIds);
        Assert.Equal(anthropicIds, openAiIds);
        var requests = server.Requests.ToArray();
        Assert.Equal("GET /api/anthropic/v1/models", requests[0].Line);
        Assert.Equal("zai-key", requests[0].ApiKey);
        Assert.Equal("GET /api/paas/v4/models", requests[1].Line);
        Assert.Equal("Bearer zai-key", requests[1].Authorization);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => LlmProviderFactory.ListModelsAsync(LlmConnectorCatalog.Require("openai-compatible"), null));
    }

    /// <summary>
    /// <c>venice/&lt;model&gt;</c> resolves to Venice's endpoint and sends
    /// <c>venice_parameters.include_venice_system_prompt: false</c> on plain and streamed requests; a route with
    /// no model is refused because Venice has no default model.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-131 VeniceRouteSwitchesOffVeniceSystemPrompt")]
    public async Task VeniceRouteSwitchesOffVeniceSystemPrompt()
    {
        var route = ModelRouter.Require("venice/zai-org-glm-5-1");
        Assert.Equal("https://api.venice.ai/api/v1", route.Endpoint);
        var provider = Assert.IsType<OpenAICompatibleLlmProvider>(LlmProviderFactory.Create(route, "venice-key"));
        Assert.Equal(new Uri("https://api.venice.ai/api/v1/chat/completions"), provider.EffectiveCompletionsUri);
        Assert.Throws<InvalidOperationException>(() => ModelRouter.Require("venice/"));

        using var server = new LoopbackServer(ChatCompletionReply);
        var llm = LlmProviderFactory.CreateForModel("venice/zai-org-glm-5-1", "venice-key", endpoint: server.BaseUrl + "/api/v1");
        await llm.ChatAsync([ChatMessage.User("hi")]);

        var request = Assert.Single(server.Requests);
        Assert.Equal("POST /api/v1/chat/completions", request.Line);
        using var body = JsonDocument.Parse(request.Body);
        Assert.False(body.RootElement.GetProperty("venice_parameters").GetProperty("include_venice_system_prompt").GetBoolean());
        Assert.Equal("zai-org-glm-5-1", body.RootElement.GetProperty("model").GetString());

        var builderLlm = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new TestDoubles.FakeEmbeddingProvider())
            .UseSqliteVec(Path.Combine(Path.GetTempPath(), $"trvenice-{Guid.NewGuid():N}.db"))
            .UseLlmForModel("venice/zai-org-glm-5-1", "venice-key")
            .CreateLlmProvider();
        Assert.NotNull(Assert.IsType<OpenAICompatibleLlmProvider>(builderLlm).BodyFields);
    }

    /// <summary>
    /// The streamed request to Venice carries the same switch, and the generic OpenAI-compatible route sends
    /// no Venice field at all.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-131 VeniceStreamCarriesSwitchAndGenericRouteDoesNot")]
    public async Task VeniceStreamCarriesSwitchAndGenericRouteDoesNot()
    {
        using var server = new LoopbackServer(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream");

        var venice = LlmProviderFactory.CreateForModel("venice/zai-org-glm-5-1", "k", endpoint: server.BaseUrl + "/api/v1");
        await foreach (var _ in venice.CompleteStreamAsync("hi"))
        {
        }

        var generic = LlmProviderFactory.CreateForModel($"openai-compatible/zai-org-glm-5-1@{server.BaseUrl}/api/v1", "k");
        await foreach (var _ in generic.CompleteStreamAsync("hi"))
        {
        }

        var requests = server.Requests.ToArray();
        using var veniceBody = JsonDocument.Parse(requests[0].Body);
        Assert.True(veniceBody.RootElement.GetProperty("stream").GetBoolean());
        Assert.True(veniceBody.RootElement.TryGetProperty("venice_parameters", out _));
        using var genericBody = JsonDocument.Parse(requests[1].Body);
        Assert.False(genericBody.RootElement.TryGetProperty("venice_parameters", out _));
    }

    /// <summary>
    /// <c>LlmConnectorCatalog.Describe()</c> lists venice, openrouter and both generic routes; every example
    /// route resolves to its own connector with a real model; local, key, endpoint and sign-in flags are right;
    /// subscription vendors that forbid sign-in are left out.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-132 DescribeListsUsableConnectorsWithResolvableExamples")]
    public void DescribeListsUsableConnectorsWithResolvableExamples()
    {
        var rows = LlmConnectorCatalog.Describe();

        foreach (var row in rows)
        {
            Assert.DoesNotContain("<model>", row.ExampleRoute, StringComparison.Ordinal);
            Assert.Equal(row.Name, ModelRouter.Require(row.ExampleRoute).Connector.Name);
            Assert.False(string.IsNullOrWhiteSpace(row.DisplayName));
        }

        var byName = rows.ToDictionary(r => r.Name);
        Assert.Equal("Venice AI", byName["venice"].DisplayName);
        Assert.Equal("https://api.venice.ai/api/v1", byName["venice"].DefaultEndpoint);
        Assert.True(byName["venice"].RequiresApiKey);
        Assert.False(byName["venice"].IsLocal);
        Assert.Contains("openrouter", byName.Keys);
        Assert.True(byName["openai-compatible"].RequiresEndpoint);
        Assert.True(byName["openai-compatible"].RequiresApiKey);
        Assert.True(byName["anthropic-compatible"].RequiresApiKey);
        Assert.Null(byName["openai-compatible"].DefaultEndpoint);
        Assert.Contains("@https://", byName["anthropic-compatible"].ExampleRoute, StringComparison.Ordinal);
        Assert.True(byName["ollama"].IsLocal);
        Assert.False(byName["ollama"].RequiresApiKey);
        Assert.True(byName["chatgpt-subscription"].RequiresSignIn);
        Assert.DoesNotContain("claude-subscription", byName.Keys);
    }

    /// <summary>
    /// The Anthropic provider posts to <c>{endpoint}/v1/messages</c> keeping the endpoint's path, does not add a
    /// second <c>/v1</c>, and still reaches Anthropic's own endpoint by default.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-133 AnthropicProviderKeepsEndpointPath")]
    public void AnthropicProviderKeepsEndpointPath()
    {
        Assert.Equal(new Uri("https://api.z.ai/api/anthropic/v1/messages"),
            new AnthropicLlmProvider("k", "glm-4.6", "https://api.z.ai/api/anthropic").EffectiveMessagesUri);
        Assert.Equal(new Uri("https://api.z.ai/api/anthropic/v1/messages"),
            new AnthropicLlmProvider("k", "glm-4.6", "https://api.z.ai/api/anthropic/").EffectiveMessagesUri);
        Assert.Equal(new Uri("https://proxy.example/v1/messages"),
            new AnthropicLlmProvider("k", "m", "https://proxy.example/v1").EffectiveMessagesUri);
        Assert.Equal(new Uri("https://api.anthropic.com/v1/messages"),
            new AnthropicLlmProvider("k").EffectiveMessagesUri);
    }

    /// <summary>One request seen by the loopback server.</summary>
    /// <param name="Line">"METHOD /path".</param>
    /// <param name="Authorization">The Authorization header, or null.</param>
    /// <param name="ApiKey">The x-api-key header, or null.</param>
    /// <param name="Body">The request body.</param>
    private sealed record SeenRequest(string Line, string? Authorization, string? ApiKey, string Body);

    /// <summary>A loopback HTTP server that answers every request with one body and records each request.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly string body;
        private readonly string contentType;

        /// <summary>Starts the server on a free loopback port.</summary>
        /// <param name="body">The body every request gets.</param>
        /// <param name="contentType">The reply's content type.</param>
        public LoopbackServer(string body, string contentType = "application/json")
        {
            this.body = body;
            this.contentType = contentType;
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

        /// <summary>Gets the requests seen, in order.</summary>
        public ConcurrentQueue<SeenRequest> Requests { get; } = new();

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
                    Requests.Enqueue(new SeenRequest(
                        $"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}",
                        context.Request.Headers["Authorization"],
                        context.Request.Headers["x-api-key"],
                        await reader.ReadToEndAsync()));
                }

                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentType = contentType;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
    }
}
