using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;
using TechieRag.Services;
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

    /// <summary>
    /// A Venice non-streamed reply that carries <c>completion_tokens_details.reasoning_tokens</c> is parsed
    /// into <see cref="TokenUsage.ReasoningTokens"/>, reported on the response and raised in the completion
    /// event (Lekhak TR-RAG-009).
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-135 VeniceNonStreamedReplyReportsReasoningTokens")]
    public async Task VeniceNonStreamedReplyReportsReasoningTokens()
    {
        const string reply =
            """{"id":"x","object":"chat.completion","model":"qwen-3-6-plus","choices":[{"index":0,"message":{"role":"assistant","content":"5"},"finish_reason":"stop"}],"usage":{"prompt_tokens":22,"completion_tokens":191,"total_tokens":213,"completion_tokens_details":{"reasoning_tokens":173}}}""";

        using var server = new LoopbackServer(reply);
        var llm = LlmProviderFactory.CreateForModel("venice/qwen-3-6-plus", "k", endpoint: server.BaseUrl + "/api/v1");

        LlmCompletionEventArgs? captured = null;
        llm.OnCompletionCompleted += (_, args) => captured = args;

        var response = await llm.ChatAsync([ChatMessage.User("Count from 1 to 5")], new LlmCompletionOptions { CallerTag = "test-agent" });

        Assert.Equal(22, response.Usage.InputTokens);
        Assert.Equal(191, response.Usage.OutputTokens);
        Assert.Equal(173, response.Usage.ReasoningTokens);
        Assert.False(response.Usage.IsEstimated);
        Assert.Equal("venice/qwen-3-6-plus", response.Usage.Route);
        Assert.Equal("test-agent", response.Usage.CallerTag);

        Assert.NotNull(captured);
        Assert.Equal(173, captured!.ReasoningTokens);
        Assert.Equal("venice/qwen-3-6-plus", captured.Route);
        Assert.Equal("test-agent", captured.CallerTag);
    }

    /// <summary>
    /// A Venice streamed reply that carries a final usage chunk with reasoning tokens reports them on the
    /// completed event and in the aggregated stream usage (Lekhak TR-RAG-008 / TR-RAG-009).
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-135 VeniceStreamedReplyReportsReasoningTokens")]
    public async Task VeniceStreamedReplyReportsReasoningTokens()
    {
        const string stream =
            "data: {\"choices\":[{\"delta\":{\"content\":\"\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"5\"}}]}\n\n" +
            "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":22,\"completion_tokens\":191,\"total_tokens\":213,\"completion_tokens_details\":{\"reasoning_tokens\":173}}}\n\n" +
            "data: [DONE]\n\n";

        using var server = new LoopbackServer(stream, "text/event-stream");
        var llm = LlmProviderFactory.CreateForModel("venice/qwen-3-6-plus", "k", endpoint: server.BaseUrl + "/api/v1");

        LlmCompletionEventArgs? captured = null;
        llm.OnCompletionCompleted += (_, args) => captured = args;

        var events = new List<LlmStreamEvent>();
        await foreach (var ev in llm.ChatStreamEventsAsync([ChatMessage.User("Count from 1 to 5")], new LlmCompletionOptions { CallerTag = "stream-agent" }))
        {
            events.Add(ev);
        }

        var completed = events.Last();
        Assert.Equal(LlmStreamEventKind.Completed, completed.Kind);
        Assert.NotNull(completed.Usage);
        Assert.Equal(22, completed.Usage!.InputTokens);
        Assert.Equal(191, completed.Usage.OutputTokens);
        Assert.Equal(173, completed.Usage.ReasoningTokens);
        Assert.Equal("venice/qwen-3-6-plus", completed.Usage.Route);
        Assert.Equal("stream-agent", completed.Usage.CallerTag);

        Assert.NotNull(captured);
        Assert.Equal(173, captured!.ReasoningTokens);
    }

    /// <summary>
    /// <see cref="LlmCompletionOptions.DisableReasoning"/> maps to Venice's own
    /// <c>venice_parameters.disable_thinking</c> and <c>strip_thinking_response</c>; <c>MaxReasoningTokens</c>
    /// maps to <c>max_thinking_tokens</c> (Lekhak TR-RAG-009).
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-135 VeniceReasoningControlsAreMapped")]
    public async Task VeniceReasoningControlsAreMapped()
    {
        const string reply =
            """{"id":"x","object":"chat.completion","model":"qwen-3-6-plus","choices":[{"index":0,"message":{"role":"assistant","content":"5"},"finish_reason":"stop"}],"usage":{"prompt_tokens":22,"completion_tokens":20,"total_tokens":42}}""";

        using var server = new LoopbackServer(reply);
        var llm = LlmProviderFactory.CreateForModel("venice/qwen-3-6-plus", "k", endpoint: server.BaseUrl + "/api/v1");
        await llm.ChatAsync(
            [ChatMessage.User("Count from 1 to 5")],
            new LlmCompletionOptions { DisableReasoning = true, MaxReasoningTokens = 50 });

        var request = Assert.Single(server.Requests);
        using var body = JsonDocument.Parse(request.Body);
        var veniceParams = body.RootElement.GetProperty("venice_parameters");
        Assert.True(veniceParams.GetProperty("disable_thinking").GetBoolean());
        Assert.True(veniceParams.GetProperty("strip_thinking_response").GetBoolean());
        Assert.Equal(50, veniceParams.GetProperty("max_thinking_tokens").GetInt32());
        Assert.False(veniceParams.GetProperty("include_venice_system_prompt").GetBoolean());
    }

    /// <summary>
    /// A host can cap the request timeout on <see cref="LlmCompletionOptions.RequestTimeout"/> so a call is
    /// not cut off at the provider's old 120-second default (Lekhak TR-RAG-010).
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-136 VeniceRequestTimeoutIsHonoured")]
    public async Task VeniceRequestTimeoutIsHonoured()
    {
        using var server = new LoopbackServer(
            """{"id":"x","object":"chat.completion","model":"qwen-3-6-plus","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""",
            delay: TimeSpan.FromMilliseconds(300));

        var llm = LlmProviderFactory.CreateForModel("venice/qwen-3-6-plus", "k", endpoint: server.BaseUrl + "/api/v1");

        // A short per-call timeout cancels before the slow reply arrives.
        var shortCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => llm.ChatAsync([ChatMessage.User("hi")], new LlmCompletionOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) }, shortCts.Token));

        // A long per-call timeout lets the slow reply finish even though the old default was 120 s.
        var response = await llm.ChatAsync(
            [ChatMessage.User("hi")],
            new LlmCompletionOptions { RequestTimeout = TimeSpan.FromSeconds(2) });
        Assert.Equal("ok", response.Content);
    }

    /// <summary>
    /// An LM Studio non-streamed reply carries the route and caller tag on the response usage and in the
    /// completion event, using the same OpenAI-compatible shape as Venice.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-135 LmStudioRouteAndCallerTagAreReported")]
    public async Task LmStudioRouteAndCallerTagAreReported()
    {
        using var server = new LoopbackServer(ChatCompletionReply);
        var llm = LlmProviderFactory.CreateForModel("lmstudio/qwen2.5", null, endpoint: server.BaseUrl + "/v1");

        LlmCompletionEventArgs? captured = null;
        llm.OnCompletionCompleted += (_, args) => captured = args;

        var response = await llm.ChatAsync(
            [ChatMessage.User("hi")],
            new LlmCompletionOptions { CallerTag = "lmstudio-agent" });

        Assert.Equal("lmstudio/qwen2.5", response.Usage.Route);
        Assert.Equal("lmstudio-agent", response.Usage.CallerTag);
        Assert.NotNull(captured);
        Assert.Equal("lmstudio/qwen2.5", captured!.Route);
        Assert.Equal("lmstudio-agent", captured.CallerTag);
    }

    /// <summary>
    /// An Ollama non-streamed reply carries the route and caller tag on the response usage and in the
    /// completion event.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-135 OllamaRouteAndCallerTagAreReported")]
    public async Task OllamaRouteAndCallerTagAreReported()
    {
        const string reply =
            """{"message":{"role":"assistant","content":"Hello"},"done":true,"done_reason":"stop","prompt_eval_count":3,"eval_count":2}""";

        using var server = new LoopbackServer(reply);
        var llm = LlmProviderFactory.CreateForModel("ollama/llama3.2", null, endpoint: server.BaseUrl);

        LlmCompletionEventArgs? captured = null;
        llm.OnCompletionCompleted += (_, args) => captured = args;

        var response = await llm.ChatAsync(
            [ChatMessage.User("hi")],
            new LlmCompletionOptions { CallerTag = "ollama-agent" });

        Assert.Equal("Hello", response.Content);
        Assert.Equal("ollama/llama3.2", response.Usage.Route);
        Assert.Equal("ollama-agent", response.Usage.CallerTag);
        Assert.NotNull(captured);
        Assert.Equal("ollama/llama3.2", captured!.Route);
        Assert.Equal("ollama-agent", captured.CallerTag);
    }

    /// <summary>
    /// An Anthropic non-streamed reply carries the route and caller tag on the response usage and in the
    /// completion event.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-135 AnthropicRouteAndCallerTagAreReported")]
    public async Task AnthropicRouteAndCallerTagAreReported()
    {
        const string reply =
            """{"id":"msg_1","type":"message","role":"assistant","model":"claude-3","content":[{"type":"text","text":"Hello"}],"stop_reason":"end_turn","usage":{"input_tokens":3,"output_tokens":1}}""";

        using var server = new LoopbackServer(reply);
        var llm = LlmProviderFactory.CreateForModel("anthropic/claude-3", "k", endpoint: server.BaseUrl + "/api/anthropic");

        LlmCompletionEventArgs? captured = null;
        llm.OnCompletionCompleted += (_, args) => captured = args;

        var response = await llm.ChatAsync(
            [ChatMessage.User("hi")],
            new LlmCompletionOptions { CallerTag = "anthropic-agent" });

        Assert.Equal("Hello", response.Content);
        Assert.Equal("anthropic/claude-3", response.Usage.Route);
        Assert.Equal("anthropic-agent", response.Usage.CallerTag);
        Assert.NotNull(captured);
        Assert.Equal("anthropic/claude-3", captured!.Route);
        Assert.Equal("anthropic-agent", captured.CallerTag);
    }

    /// <summary>
    /// A per-call request timeout cancels a slow LM Studio provider before the old 120-second default.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-136 RequestTimeoutCancelsSlowProvider")]
    public async Task RequestTimeoutCancelsSlowProvider()
    {
        using var server = new LoopbackServer(
            """{"id":"x","object":"chat.completion","model":"qwen2.5","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""",
            delay: TimeSpan.FromMilliseconds(300));

        var llm = LlmProviderFactory.CreateForModel("lmstudio/qwen2.5", null, endpoint: server.BaseUrl + "/v1");
        var hostCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => llm.ChatAsync(
                [ChatMessage.User("hi")],
                new LlmCompletionOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) },
                hostCts.Token));

        Assert.True(hostCts.Token.IsCancellationRequested || ex.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <see cref="BudgetEnforcingLlmProvider"/> refuses further calls once the session budget is spent, raising
    /// <see cref="TechieRagBudgetExceededException"/> before the underlying provider is asked.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-135 BudgetEnforcingProviderRefusesCallsOnceSpent")]
    public void BudgetEnforcingProviderRefusesCallsOnceSpent()
    {
        var tracker = new TokenUsageTracker();
        tracker.SetBudget(new UsageBudget { MaxTotalTokens = 10, BlockOnExceeded = true });
        tracker.RecordUsage(new TokenUsage { InputTokens = 5, OutputTokens = 6, ModelName = "m", ProviderName = "p" });

        var fake = new TestDoubles.FakeLlmProvider { ModelName = "m" };
        var guarded = new BudgetEnforcingLlmProvider(fake, tracker);

        var ex = Assert.Throws<TechieRagBudgetExceededException>(
            () => guarded.ChatAsync([ChatMessage.User("hi")], new LlmCompletionOptions { CallerTag = "agent-1" }).GetAwaiter().GetResult());
        Assert.Contains("agent-1", ex.Message, StringComparison.Ordinal);
        Assert.Equal("agent-1", ex.CallerTag);
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
        private readonly TimeSpan delay;

        /// <summary>Starts the server on a free loopback port.</summary>
        /// <param name="body">The body every request gets.</param>
        /// <param name="contentType">The reply's content type.</param>
        /// <param name="delay">Artificial delay before the response body is written.</param>
        public LoopbackServer(string body, string contentType = "application/json", TimeSpan? delay = null)
        {
            this.body = body;
            this.contentType = contentType;
            this.delay = delay ?? TimeSpan.Zero;
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

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay);
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
