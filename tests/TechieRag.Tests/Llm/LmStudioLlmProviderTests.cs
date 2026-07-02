using System.Net;
using System.Text;
using System.Text.Json;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Unit tests for <see cref="LmStudioLlmProvider"/> tool-calling support (TR-RAG-006 / REQ-RAG-009).
/// </summary>
public class LmStudioLlmProviderTests
{
    /// <summary>
    /// Verifies the provider advertises tool-calling capability so the agent loop supplies tool definitions.
    /// </summary>
    [Fact]
    public void SupportsToolCallingIsTrue()
    {
        var provider = new LmStudioLlmProvider("http://localhost:1234");
        Assert.True(provider.SupportsToolCalling);
    }

    /// <summary>
    /// Verifies that when tool definitions are supplied via <see cref="LlmCompletionOptions.Tools"/>,
    /// the serialized request body sent to /v1/chat/completions contains a "tools" array carrying the
    /// declared function (get_weather) and its parameter schema.
    /// </summary>
    [Fact]
    public async Task ChatSerializesToolsArrayWhenToolsSupplied()
    {
        var handler = new StubHandler(BuildToolCallResponseJson());
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234") };
        var provider = new LmStudioLlmProvider(httpClient, "qwen2.5-coder-32b-instruct");

        var options = new LlmCompletionOptions
        {
            Tools = new List<ToolDefinition>
            {
                new()
                {
                    Name = "get_weather",
                    Description = "Get the current weather for a city.",
                    ParametersSchema = """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}"""
                }
            },
            ToolChoice = "auto"
        };

        await provider.ChatAsync(new List<ChatMessage> { ChatMessage.User("Weather in Paris?") }, options);

        Assert.NotNull(handler.CapturedBody);
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("tools", out var tools));
        Assert.Equal(JsonValueKind.Array, tools.ValueKind);
        var fn = tools[0].GetProperty("function");
        Assert.Equal("get_weather", fn.GetProperty("name").GetString());
        Assert.True(fn.TryGetProperty("parameters", out _));
        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());
    }

    /// <summary>
    /// Verifies that a canned response with finish_reason "tool_calls" and a get_weather tool call is
    /// parsed into <see cref="LlmResponse.ToolCalls"/> with <see cref="LlmResponse.HasToolCalls"/> true.
    /// </summary>
    [Fact]
    public async Task ChatParsesToolCallsFromResponse()
    {
        var handler = new StubHandler(BuildToolCallResponseJson());
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234") };
        var provider = new LmStudioLlmProvider(httpClient, "qwen2.5-coder-32b-instruct");

        var response = await provider.ChatAsync(new List<ChatMessage> { ChatMessage.User("Weather in Paris?") });

        Assert.True(response.HasToolCalls);
        Assert.NotNull(response.ToolCalls);
        var call = Assert.Single(response.ToolCalls!);
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("call_abc123", call.Id);
        Assert.Contains("Paris", call.ArgumentsJson);
        Assert.Equal("tool_calls", response.FinishReason);
    }

    private static string BuildToolCallResponseJson() => """
    {
      "id": "chatcmpl-1",
      "model": "qwen2.5-coder-32b-instruct",
      "choices": [
        {
          "index": 0,
          "message": {
            "role": "assistant",
            "content": null,
            "tool_calls": [
              {
                "id": "call_abc123",
                "type": "function",
                "function": { "name": "get_weather", "arguments": "{\"city\":\"Paris\"}" }
              }
            ]
          },
          "finish_reason": "tool_calls"
        }
      ],
      "usage": { "prompt_tokens": 12, "completion_tokens": 8, "total_tokens": 20 }
    }
    """;

    /// <summary>Stub handler that captures the request body and returns a canned response.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string responseJson;

        public string? CapturedBody { get; private set; }

        public StubHandler(string responseJson) => this.responseJson = responseJson;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
