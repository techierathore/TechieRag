using System.Net;
using System.Text;
using System.Text.Json;
using TechieRag.Llm;
using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// The Anthropic provider leaves out the sampling options a Claude model refuses (REQ-RAG-158, Sevak TR-RAG-063).
/// </summary>
/// <remarks>
/// The service is stood in by <see cref="SamplingRefusingService"/>, which answers exactly as Anthropic answered Sevak
/// on 9 November 2026 when a refusing model gets a sampling option: HTTP 400, <c>invalid_request_error</c>,
/// "temperature is deprecated for this model". That body is the recorded answer as Sevak reported it; no live call
/// was made from this machine (no key). The live check is <see cref="LiveAnthropicSamplingTests"/>.
/// </remarks>
public class AnthropicSamplingTests
{
    /// <summary>The 400 body Anthropic returns for a sampling option on a model that refuses it.</summary>
    internal const string RecordedTemperatureRefusal =
        """{"type":"error","error":{"type":"invalid_request_error","message":"temperature is deprecated for this model."}}""";

    private const string OkReply =
        """{"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-5-5","content":[{"type":"text","text":"Paris."}],"stop_reason":"end_turn","usage":{"input_tokens":12,"output_tokens":3}}""";

    private const string OkStream = """
        event: message_start
        data: {"type":"message_start","message":{"model":"claude-sonnet-5-5","usage":{"input_tokens":12,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Paris."}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":3}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    private static readonly IReadOnlyList<ChatMessage> Question = [ChatMessage.User("What is the capital of France?")];

    /// <summary>
    /// Sevak's repro: <c>ChatAsync</c> on <c>claude-sonnet-5-5</c> with <c>Temperature = 0.3</c> answers instead of
    /// failing with the 400, and the request it sent carries no temperature or top_p.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-158 SonnetFiveFiveChatWithTemperatureSucceeds")]
    public async Task SonnetFiveFiveChatWithTemperatureSucceeds()
    {
        var service = new SamplingRefusingService(refuses: true);
        var provider = Provider(service, "claude-sonnet-5-5");

        var response = await provider.ChatAsync(Question, new LlmCompletionOptions { Temperature = 0.3f, TopP = 0.9f });

        Assert.Equal("Paris.", response.Content);
        var body = Assert.Single(service.RequestBodies);
        Assert.DoesNotContain("\"temperature\"", body);
        Assert.DoesNotContain("\"top_p\"", body);
    }

    /// <summary>The same holds inside <see cref="AgentLoopRunner"/>, which copies the host's temperature into every call.</summary>
    [Fact(DisplayName = "REQ-RAG-158 AgentLoopOnSonnetFiveFiveWithTemperatureSucceeds")]
    public async Task AgentLoopOnSonnetFiveFiveWithTemperatureSucceeds()
    {
        var service = new SamplingRefusingService(refuses: true);
        var runner = new AgentLoopRunner(Provider(service, "claude-sonnet-5-5"), new ToolRegistry());

        var response = await runner.RunAsync([ChatMessage.User("What is the capital of France?")], new LlmCompletionOptions { Temperature = 0.3f });

        Assert.Equal("Paris.", response.Content);
        Assert.DoesNotContain("\"temperature\"", Assert.Single(service.RequestBodies));
    }

    /// <summary>The streaming path leaves the options out the same way.</summary>
    [Fact(DisplayName = "REQ-RAG-158 SonnetFiveFiveStreamWithTemperatureSucceeds")]
    public async Task SonnetFiveFiveStreamWithTemperatureSucceeds()
    {
        var service = new SamplingRefusingService(refuses: true);
        var provider = Provider(service, "claude-sonnet-5-5");

        var text = new StringBuilder();
        await foreach (var piece in provider.ChatStreamAsync(Question, new LlmCompletionOptions { Temperature = 0.3f }))
        {
            text.Append(piece);
        }

        Assert.Equal("Paris.", text.ToString());
        Assert.DoesNotContain("\"temperature\"", Assert.Single(service.RequestBodies));
    }

    /// <summary>A model that takes sampling options still gets them: one option set works for every model.</summary>
    [Fact(DisplayName = "REQ-RAG-158 OlderModelStillGetsTemperatureAndTopP")]
    public async Task OlderModelStillGetsTemperatureAndTopP()
    {
        var service = new SamplingRefusingService(refuses: false);
        var provider = Provider(service, "claude-sonnet-4-5-20250929");

        await provider.ChatAsync(Question, new LlmCompletionOptions { Temperature = 0.3f, TopP = 0.9f });

        using var body = JsonDocument.Parse(Assert.Single(service.RequestBodies));
        Assert.Equal(0.3, body.RootElement.GetProperty("temperature").GetDouble(), 3);
        Assert.Equal(0.9, body.RootElement.GetProperty("top_p").GetDouble(), 3);
    }

    /// <summary>
    /// A model the rule does not know yet that refuses a sampling option: the call is sent once more without the
    /// options and succeeds, and the next call to that model leaves them out from the start (one request, not two).
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-158 UnknownRefusingModelIsRetriedOnceWithoutSampling")]
    public async Task UnknownRefusingModelIsRetriedOnceWithoutSampling()
    {
        var model = "claude-nova-" + Guid.NewGuid().ToString("N")[..8];
        var service = new SamplingRefusingService(refuses: true);
        var provider = Provider(service, model);

        var first = await provider.ChatAsync(Question, new LlmCompletionOptions { Temperature = 0.3f });

        Assert.Equal("Paris.", first.Content);
        Assert.Equal(2, service.RequestBodies.Count);
        Assert.Contains("\"temperature\"", service.RequestBodies[0]);
        Assert.DoesNotContain("\"temperature\"", service.RequestBodies[1]);

        await provider.ChatAsync(Question, new LlmCompletionOptions { Temperature = 0.3f });
        Assert.Equal(3, service.RequestBodies.Count);
        Assert.DoesNotContain("\"temperature\"", service.RequestBodies[2]);
    }

    /// <summary>A 400 that is not about a sampling option is not retried and reaches the caller with the service's text.</summary>
    [Fact(DisplayName = "REQ-RAG-158 OtherBadRequestIsNotRetried")]
    public async Task OtherBadRequestIsNotRetried()
    {
        var service = new SamplingRefusingService(refuses: false)
        {
            FixedFailure = """{"type":"error","error":{"type":"invalid_request_error","message":"max_tokens: must be at least 1"}}"""
        };
        var provider = Provider(service, "claude-sonnet-4-5-20250929");

        var exception = await Assert.ThrowsAsync<LlmHttpException>(() => provider.ChatAsync(Question, new LlmCompletionOptions { Temperature = 0.3f }));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Single(service.RequestBodies);
    }

    /// <summary>Which model names take sampling options, including platform prefixes and date suffixes.</summary>
    /// <param name="model">The model id.</param>
    /// <param name="expected">Whether it takes them.</param>
    [Theory(DisplayName = "REQ-RAG-158 SamplingRuleKnowsEachModel")]
    [InlineData("claude-sonnet-5-5", false)]
    [InlineData("claude-opus-5-5", false)]
    [InlineData("claude-haiku-5-5", false)]
    [InlineData("claude-sonnet-5", false)]
    [InlineData("claude-opus-5", false)]
    [InlineData("claude-fable-5-2", false)]
    [InlineData("claude-mythos-5-1", false)]
    [InlineData("claude-opus-4-8", false)]
    [InlineData("claude-opus-4-7", false)]
    [InlineData("anthropic.claude-sonnet-5-5", false)]
    [InlineData("claude-opus-4-6", true)]
    [InlineData("claude-sonnet-4-6", true)]
    [InlineData("claude-haiku-4-5", true)]
    [InlineData("claude-sonnet-4-5-20250929", true)]
    [InlineData("claude-sonnet-4-20250514", true)]
    [InlineData("claude-opus-4-1-20250805", true)]
    [InlineData("claude-opus-4-5@20251101", true)]
    [InlineData("claude-3-5-haiku-20241022", true)]
    [InlineData("glm-4.6", true)]
    public void SamplingRuleKnowsEachModel(string model, bool expected) =>
        Assert.Equal(expected, AnthropicSamplingSupport.TakesSampling(model));

    private static AnthropicLlmProvider Provider(HttpMessageHandler handler, string model) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") }, model);

    /// <summary>
    /// Stands in for the Messages API: a refusing model answers a sampling option with the recorded 400, anything
    /// else gets a normal reply (JSON, or an event stream when the request asked to stream).
    /// </summary>
    private sealed class SamplingRefusingService(bool refuses) : HttpMessageHandler
    {
        /// <summary>Gets each request body, in order.</summary>
        public List<string> RequestBodies { get; } = [];

        /// <summary>Gets or sets a 400 body returned for every request, whatever it carries.</summary>
        public string? FixedFailure { get; init; }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var carriesSampling = root.TryGetProperty("temperature", out _) || root.TryGetProperty("top_p", out _) || root.TryGetProperty("top_k", out _);
            if (FixedFailure is not null || (refuses && carriesSampling))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(FixedFailure ?? RecordedTemperatureRefusal, Encoding.UTF8, "application/json")
                };
            }

            var streaming = root.TryGetProperty("stream", out var stream) && stream.ValueKind == JsonValueKind.True;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = streaming
                    ? new StringContent(OkStream, Encoding.UTF8, "text/event-stream")
                    : new StringContent(OkReply, Encoding.UTF8, "application/json")
            };
        }
    }
}
