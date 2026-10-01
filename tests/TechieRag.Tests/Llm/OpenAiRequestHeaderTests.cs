using System.Net;
using System.Text;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Wire-format tests for extra request headers and the per-conversation session header on an
/// OpenAI-compatible model (REQ-RAG-109, Chatur TR-RAG-003).
/// </summary>
/// <remarks>
/// OpenCode Go answers 400 <c>MissingSessionID</c> to a request without <c>x-opencode-session</c>. These
/// prove what TechieRag sends; <see cref="LiveOpenCodeGoTests"/> proves the service accepts it.
/// </remarks>
public class OpenAiRequestHeaderTests
{
    private const string ChatResponseJson =
        """{"model":"m","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"OK"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""";

    private static readonly IReadOnlyList<ChatMessage> Question = [ChatMessage.User("Say OK")];

    /// <summary>
    /// Acceptance of REQ-RAG-109: the caller's session id travels in the configured session header, the
    /// same id on every turn of the conversation.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-109 SessionIdTravelsInSessionHeader")]
    public async Task SessionIdTravelsInSessionHeader()
    {
        var handler = new HeaderCapturingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://go.test") };
        var provider = new OpenAICompatibleLlmProvider(client, "kimi-k2.7-code", sessionHeader: "x-opencode-session");
        var options = new LlmCompletionOptions { SessionId = "conversation-42" };

        await provider.ChatAsync(Question, options);
        await provider.ChatAsync(Question, options);

        Assert.Equal(["conversation-42", "conversation-42"], handler.Values("x-opencode-session"));
    }

    /// <summary>Without a caller id, one provider sends one stable id of its own, streaming or not.</summary>
    [Fact(DisplayName = "REQ-RAG-109 ProviderKeepsOneSessionIdWhenNoneGiven")]
    public async Task ProviderKeepsOneSessionIdWhenNoneGiven()
    {
        var handler = new HeaderCapturingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://go.test") };
        var provider = new OpenAICompatibleLlmProvider(client, "kimi-k2.7-code", sessionHeader: "x-opencode-session");

        await provider.ChatAsync(Question);
        await foreach (var _ in provider.ChatStreamEventsAsync(Question))
        {
        }

        var values = handler.Values("x-opencode-session");
        Assert.Equal(2, values.Count);
        Assert.False(string.IsNullOrWhiteSpace(values[0]));
        Assert.Equal(values[0], values[1]);
    }

    /// <summary>A provider with no session header sends none, so other services see no change.</summary>
    [Fact]
    public async Task NoSessionHeaderUnlessConfigured()
    {
        var handler = new HeaderCapturingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test") };
        var provider = new OpenAICompatibleLlmProvider(client, "gpt-4o");

        await provider.ChatAsync(Question, new LlmCompletionOptions { SessionId = "conversation-42" });

        Assert.Empty(handler.Values("x-opencode-session"));
    }

    /// <summary>Fixed headers go on every request, and one named there replaces the library's own value.</summary>
    [Fact(DisplayName = "REQ-RAG-109 FixedHeadersAreSentAndReplaceDefaults")]
    public async Task FixedHeadersAreSentAndReplaceDefaults()
    {
        var handler = new HeaderCapturingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test") };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TechieRag/1.0.0");
        var headers = new Dictionary<string, string> { ["User-Agent"] = "chatur/1.0", ["x-client-tag"] = "chatur" };
        var provider = new OpenAICompatibleLlmProvider(client, "gpt-4o", headers: headers);

        await provider.ChatAsync(Question);

        Assert.Equal(["chatur/1.0"], handler.Values("User-Agent"));
        Assert.Equal(["chatur"], handler.Values("x-client-tag"));
    }

    /// <summary>
    /// Acceptance of REQ-RAG-109: the <c>opencode-go</c> connector carries OpenCode Go's endpoint and session
    /// header, and a provider built from it by name or by <c>opencode-go/model</c> sends that header.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-109 OpenCodeGoConnectorSendsSessionHeader")]
    public void OpenCodeGoConnectorSendsSessionHeader()
    {
        var connector = LlmConnectorCatalog.Require(LlmConnectorCatalog.OpenCodeGoName);

        var fromBuilder = (OpenAICompatibleLlmProvider)new TechieRagBuilder()
            .UseConnectorLlm(LlmConnectorCatalog.OpenCodeGoName, "test-key")
            .CreateLlmProvider();
        var fromFactory = (OpenAICompatibleLlmProvider)LlmProviderFactory.CreateForModel("opencode-go/kimi-k3", "test-key");

        Assert.Equal("https://opencode.ai/zen/go/v1", connector.Endpoint);
        Assert.Equal("x-opencode-session", fromBuilder.SessionHeaderName);
        Assert.Equal("x-opencode-session", fromFactory.SessionHeaderName);
        Assert.Equal(new Uri("https://opencode.ai/zen/go/v1/chat/completions"), fromBuilder.EffectiveCompletionsUri);
    }

    /// <summary>The builder overload for any OpenAI-compatible endpoint takes headers and a session header.</summary>
    [Fact]
    public void BuilderOverloadCarriesHeaders()
    {
        var builder = new TechieRagBuilder().UseOpenAICompatibleLlm(
            "https://gateway.test/v1", "key", "m", new Dictionary<string, string> { ["x-tag"] = "1" }, "x-session");

        var provider = (OpenAICompatibleLlmProvider)builder.CreateLlmProvider();

        Assert.Equal("1", builder.GetConfig().Llm.Headers["x-tag"]);
        Assert.Equal("x-session", provider.SessionHeaderName);
    }

    /// <summary>Records the headers of every request and answers with a canned chat or stream reply.</summary>
    private sealed class HeaderCapturingHandler : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> requests = [];

        public IReadOnlyList<string> Values(string name) =>
            requests.SelectMany(r => r.Headers.TryGetValues(name, out var values) ? values : []).ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request);
            var requestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var streaming = requestBody.Contains("\"stream\":true", StringComparison.Ordinal);
            var body = streaming
                ? "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"OK\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"
                : ChatResponseJson;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, streaming ? "text/event-stream" : "application/json")
            };
        }
    }
}
