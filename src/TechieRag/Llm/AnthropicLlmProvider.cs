using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// LLM provider implementation for Anthropic Claude API.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enables LLM interactions using Anthropic's Claude models
/// via the Messages API, supporting chat, streaming, and tool use.</para>
/// <para><b>API Differences:</b> Anthropic uses a different API format than OpenAI:
/// system message is a top-level field, not in the messages array.
/// Tool use returns content blocks with type "tool_use" instead of "tool_calls".</para>
/// </remarks>
public class AnthropicLlmProvider : ILlmProvider
{
    private readonly HttpClient httpClient;
    private readonly int defaultMaxTokens;
    private readonly ILogger<AnthropicLlmProvider> logger;

    /// <inheritdoc/>
    public string Name => "Anthropic";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;

    /// <summary>
    /// Creates a new Anthropic LLM provider instance.
    /// </summary>
    /// <param name="apiKey">Anthropic API key.</param>
    /// <param name="model">Model name (e.g., "claude-sonnet-4-5-20250929").</param>
    /// <param name="endpoint">API endpoint (defaults to https://api.anthropic.com).</param>
    /// <param name="maxTokens">Default max output tokens.</param>
    /// <param name="logger">Logger instance.</param>
    public AnthropicLlmProvider(string apiKey, string model = "claude-sonnet-4-5-20250929", string? endpoint = null, int maxTokens = 2048, ILogger<AnthropicLlmProvider>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);

        ModelName = model;
        defaultMaxTokens = maxTokens;
        this.logger = logger ?? NullLogger<AnthropicLlmProvider>.Instance;

        httpClient = new HttpClient
        {
            BaseAddress = new Uri((endpoint ?? "https://api.anthropic.com").TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(120)
        };
        httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    /// <summary>
    /// Creates an Anthropic provider with a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>Test seam: allows a stubbed <see cref="HttpMessageHandler"/> to intercept requests.</remarks>
    /// <param name="httpClient">Pre-configured HTTP client (BaseAddress must be set).</param>
    /// <param name="model">Model name.</param>
    /// <param name="maxTokens">Default max output tokens.</param>
    /// <param name="logger">Logger instance.</param>
    internal AnthropicLlmProvider(HttpClient httpClient, string model, int maxTokens = 2048, ILogger<AnthropicLlmProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        ModelName = model;
        defaultMaxTokens = maxTokens;
        this.logger = logger ?? NullLogger<AnthropicLlmProvider>.Instance;
    }

    /// <inheritdoc/>
    public async Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(options?.SystemPrompt))
            messages.Add(ChatMessage.System(options.SystemPrompt));
        messages.Add(ChatMessage.User(prompt));

        return await ChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(options?.SystemPrompt))
            messages.Add(ChatMessage.System(options.SystemPrompt));
        messages.Add(ChatMessage.User(prompt));

        await foreach (var token in ChatStreamAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    /// <inheritdoc/>
    public async Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var request = BuildAnthropicRequest(messages, options);
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("/v1/messages", content, cancellationToken).ConfigureAwait(false);
        LlmHttpGuard.EnsureSuccess(response);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<AnthropicResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to parse Anthropic response");

        sw.Stop();

        var textContent = ExtractText(result.Content);
        var toolCalls = ExtractToolCalls(result.Content);
        var inputTokens = result.Usage?.InputTokens ?? 0;
        var outputTokens = result.Usage?.OutputTokens ?? 0;

        var llmResponse = new LlmResponse
        {
            Content = textContent,
            ToolCalls = toolCalls,
            Usage = new TokenUsage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ModelName = ModelName,
                ProviderName = Name
            },
            FinishReason = result.StopReason == "tool_use" ? "tool_calls" : result.StopReason ?? "stop",
            ModelName = result.Model ?? ModelName
        };

        RaiseCompletionEvent(inputTokens, outputTokens, sw.Elapsed, false, llmResponse.HasToolCalls);
        return llmResponse;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var request = BuildAnthropicRequest(messages, options);
        ((Dictionary<string, object>)request)["stream"] = true;
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/v1/messages") { Content = content };
        var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        LlmHttpGuard.EnsureSuccess(response);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        int totalInputTokens = 0;
        int totalOutputTokens = 0;
        var outputText = new StringBuilder();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("event: "))
            {
                var eventType = line["event: ".Length..];
                if (eventType == "message_stop") break;
                continue;
            }

            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            var eventData = JsonSerializer.Deserialize<JsonElement>(data);

            if (eventData.TryGetProperty("type", out var typeEl))
            {
                var type = typeEl.GetString();

                if (type == "message_start" && eventData.TryGetProperty("message", out var msg))
                {
                    if (msg.TryGetProperty("usage", out var usage) && usage.TryGetProperty("input_tokens", out var inputTok))
                    {
                        totalInputTokens = inputTok.GetInt32();
                    }
                }
                else if (type == "content_block_delta" && eventData.TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("text", out var text))
                    {
                        var textValue = text.GetString();
                        if (!string.IsNullOrEmpty(textValue))
                        {
                            outputText.Append(textValue);
                            yield return textValue;
                        }
                    }
                }
                else if (type == "message_delta" && eventData.TryGetProperty("usage", out var usageDelta))
                {
                    if (usageDelta.TryGetProperty("output_tokens", out var outputTok))
                    {
                        totalOutputTokens = outputTok.GetInt32();
                    }
                }
            }
        }

        sw.Stop();

        // Fallback: estimate when the API sent no usage events
        if (totalInputTokens == 0 && totalOutputTokens == 0)
        {
            totalInputTokens = messages.Sum(m => EstimateTokenCount(m.Content ?? string.Empty));
            totalOutputTokens = EstimateTokenCount(outputText.ToString());
        }

        RaiseCompletionEvent(totalInputTokens, totalOutputTokens, sw.Elapsed, true, false);
    }

    /// <inheritdoc/>
    public async Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        var jsonPrompt = $"{prompt}\n\nRespond with valid JSON only.";
        var response = await CompleteAsync(jsonPrompt, new LlmCompletionOptions
        {
            Temperature = options?.Temperature,
            MaxTokens = options?.MaxTokens,
            SystemPrompt = options?.SystemPrompt ?? "You are a helpful assistant that responds only with valid JSON."
        }, cancellationToken).ConfigureAwait(false);

        var content = response.Content?.Trim() ?? throw new InvalidOperationException("LLM returned empty response");
        if (content.StartsWith("```"))
        {
            var firstNewline = content.IndexOf('\n');
            if (firstNewline > 0) content = content[(firstNewline + 1)..];
            if (content.EndsWith("```")) content = content[..^3];
            content = content.Trim();
        }

        return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Failed to deserialize response to {typeof(T).Name}");
    }

    /// <inheritdoc/>
    public int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private object BuildAnthropicRequest(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options)
    {
        var request = new Dictionary<string, object>
        {
            ["model"] = options?.Model ?? ModelName,
            ["max_tokens"] = options?.MaxTokens ?? defaultMaxTokens
        };

        // Anthropic: system message is a top-level field
        var systemMsg = messages.FirstOrDefault(m => m.Role == "system");
        if (systemMsg is not null)
        {
            request["system"] = systemMsg.Content ?? string.Empty;
        }

        // Map non-system messages
        var apiMessages = messages
            .Where(m => m.Role != "system")
            .Select(m =>
            {
                if (m.Role == "tool")
                {
                    return new Dictionary<string, object>
                    {
                        ["role"] = "user",
                        ["content"] = new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["type"] = "tool_result",
                                ["tool_use_id"] = m.ToolCallId ?? string.Empty,
                                ["content"] = m.Content ?? string.Empty
                            }
                        }
                    };
                }

                if (m.ToolCalls is { Count: > 0 })
                {
                    var contentBlocks = new List<object>();
                    if (!string.IsNullOrEmpty(m.Content))
                    {
                        contentBlocks.Add(new Dictionary<string, object> { ["type"] = "text", ["text"] = m.Content });
                    }
                    foreach (var tc in m.ToolCalls)
                    {
                        contentBlocks.Add(new Dictionary<string, object>
                        {
                            ["type"] = "tool_use",
                            ["id"] = tc.Id,
                            ["name"] = tc.Name,
                            ["input"] = JsonSerializer.Deserialize<JsonElement>(tc.ArgumentsJson)
                        });
                    }
                    return new Dictionary<string, object> { ["role"] = "assistant", ["content"] = contentBlocks };
                }

                return new Dictionary<string, object>
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content ?? string.Empty
                };
            }).ToList();

        request["messages"] = apiMessages;

        if (options?.Temperature is not null) request["temperature"] = options.Temperature.Value;
        if (options?.TopP is not null) request["top_p"] = options.TopP.Value;
        if (options?.StopSequences is not null) request["stop_sequences"] = options.StopSequences;

        // Tool definitions
        if (options?.Tools is { Count: > 0 })
        {
            request["tools"] = options.Tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                input_schema = JsonSerializer.Deserialize<JsonElement>(t.ParametersSchema)
            }).ToList();

            if (options.ToolChoice is not null)
            {
                request["tool_choice"] = options.ToolChoice switch
                {
                    "auto" => new Dictionary<string, object> { ["type"] = "auto" },
                    "none" => new Dictionary<string, object> { ["type"] = "none" },
                    "required" => new Dictionary<string, object> { ["type"] = "any" },
                    _ => new Dictionary<string, object> { ["type"] = "tool", ["name"] = options.ToolChoice }
                };
            }
        }

        return request;
    }

    private static string? ExtractText(List<AnthropicContentBlock>? contentBlocks)
    {
        if (contentBlocks is null) return null;
        var texts = contentBlocks.Where(b => b.Type == "text").Select(b => b.Text);
        var joined = string.Join("", texts);
        return string.IsNullOrEmpty(joined) ? null : joined;
    }

    private static List<ToolCall>? ExtractToolCalls(List<AnthropicContentBlock>? contentBlocks)
    {
        if (contentBlocks is null) return null;

        var toolUses = contentBlocks
            .Where(b => b.Type == "tool_use")
            .Select(b => new ToolCall
            {
                Id = b.Id ?? Guid.NewGuid().ToString(),
                Name = b.Name ?? string.Empty,
                ArgumentsJson = b.Input is not null ? JsonSerializer.Serialize(b.Input) : "{}"
            }).ToList();

        return toolUses.Count > 0 ? toolUses : null;
    }

    private void RaiseCompletionEvent(int inputTokens, int outputTokens, TimeSpan duration, bool isStreaming, bool involvedToolCalls)
    {
        OnCompletionCompleted?.Invoke(this, new LlmCompletionEventArgs
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Duration = duration,
            ModelName = ModelName,
            ProviderName = Name,
            IsStreaming = isStreaming,
            InvolvedToolCalls = involvedToolCalls
        });
    }

    private class AnthropicResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("content")]
        public List<AnthropicContentBlock>? Content { get; set; }

        [JsonPropertyName("stop_reason")]
        public string? StopReason { get; set; }

        [JsonPropertyName("usage")]
        public AnthropicUsage? Usage { get; set; }
    }

    private class AnthropicContentBlock
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("input")]
        public JsonElement? Input { get; set; }
    }

    private class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }
}
