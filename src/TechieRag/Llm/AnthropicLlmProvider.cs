using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Diagnostics;
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
public class AnthropicLlmProvider : ILlmProvider, IMultimodalLlmProvider
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
    /// <remarks>Images are encoded as native <c>image</c> content blocks, inline or by URL (REQ-RAG-039).</remarks>
    public bool SupportsInput(ChatContentKind kind) =>
        kind is ChatContentKind.Text or ChatContentKind.Image;

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
        var cacheReadTokens = result.Usage?.CacheReadInputTokens ?? 0;
        var cacheWriteTokens = result.Usage?.CacheCreationInputTokens ?? 0;

        var llmResponse = new LlmResponse
        {
            Content = textContent,
            ToolCalls = toolCalls,
            Usage = new TokenUsage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CacheReadTokens = cacheReadTokens,
                CacheWriteTokens = cacheWriteTokens,
                ModelName = ModelName,
                ProviderName = Name
            },
            FinishReason = result.StopReason == "tool_use" ? "tool_calls" : result.StopReason ?? "stop",
            ModelName = result.Model ?? ModelName
        };

        RaiseCompletionEvent(inputTokens, outputTokens, sw.Elapsed, false, llmResponse.HasToolCalls, cacheReadTokens, cacheWriteTokens);
        return llmResponse;
    }

    /// <inheritdoc/>
    /// <remarks>The text-only projection of <see cref="ChatStreamEventsAsync"/> (REQ-RAG-067).</remarks>
    public IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        ChatStreamEventsAsync(messages, options, cancellationToken).ToTextStreamAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>Anthropic streams a <c>tool_use</c> block's input as <c>input_json_delta</c> fragments;
    /// they are assembled and the call is emitted when its block stops (REQ-RAG-067 / BRD-110).</remarks>
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamEventsAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

        var state = new AnthropicStreamState();

        // ReadLineAsync returning null is end of stream; EndOfStream would block synchronously (CA2024).
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("event: "))
            {
                if (line["event: ".Length..] == "message_stop") break;
                continue;
            }

            if (!line.StartsWith("data: ")) continue;

            var streamEvent = state.Apply(JsonSerializer.Deserialize<JsonElement>(line["data: ".Length..]));
            if (streamEvent is not null)
            {
                yield return streamEvent;
            }
        }

        var context = new StreamReadContext(Name, ModelName, messages, EstimateTokenCount, (_, _) => { });
        var usage = context.BuildUsage(state.InputTokens, state.OutputTokens, state.CacheReadTokens, state.OutputText.ToString(), state.CacheWriteTokens);
        sw.Stop();
        RaiseCompletionEvent(usage.InputTokens, usage.OutputTokens, sw.Elapsed, true, state.ToolCallCount > 0, usage.CacheReadTokens, usage.CacheWriteTokens);

        var finishReason = state.StopReason == "tool_use" ? "tool_calls" : state.StopReason ?? (state.ToolCallCount > 0 ? "tool_calls" : "stop");
        yield return LlmStreamEvent.FromCompleted(usage, finishReason, ModelName);
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

        var cache = options?.PromptCache;

        // Anthropic: system message is a top-level field
        var systemMsg = messages.FirstOrDefault(m => m.Role == "system");
        if (systemMsg is not null)
        {
            // The plain-string form has nowhere to hang cache_control, so caching the system prompt
            // forces the block-array form (REQ-RAG-043). Uncached callers keep the simpler shape.
            if (cache?.CacheSystemPrompt == true)
            {
                var systemBlock = new Dictionary<string, object>
                {
                    ["type"] = "text",
                    ["text"] = systemMsg.Content ?? string.Empty,
                    ["cache_control"] = BuildCacheControl(cache)
                };
                request["system"] = new List<object> { systemBlock };
            }
            else
            {
                request["system"] = systemMsg.Content ?? string.Empty;
            }
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
                    ApplyCacheBoundary(contentBlocks, m, cache);
                    return new Dictionary<string, object> { ["role"] = "assistant", ["content"] = contentBlocks };
                }

                // Multimodal (REQ-RAG-039) and cache breakpoints (REQ-RAG-043) both need the block-array
                // form. Everything else stays on the plain string, which is what the vast majority of
                // messages are and what the API documents as the common case.
                var needsBlocks = m.Parts is { Count: > 0 } || (cache is not null && m.CacheBoundary);
                if (needsBlocks)
                {
                    var blocks = BuildContentBlocks(m);
                    ApplyCacheBoundary(blocks, m, cache);
                    return new Dictionary<string, object> { ["role"] = m.Role, ["content"] = blocks };
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
            var toolDefinitions = options.Tools.Select(t => new Dictionary<string, object>
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["input_schema"] = JsonSerializer.Deserialize<JsonElement>(t.ParametersSchema)
            }).ToList();

            // Anthropic caches the prefix up to a breakpoint, so the marker goes on the LAST tool:
            // that covers the whole tool block and the system prompt ahead of it in one entry
            // (REQ-RAG-043). Marking the first tool would cache almost nothing.
            if (cache?.CacheToolDefinitions == true && toolDefinitions.Count > 0)
            {
                toolDefinitions[^1]["cache_control"] = BuildCacheControl(cache);
            }

            request["tools"] = toolDefinitions;

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

    /// <summary>Builds Anthropic content blocks for one message, images included (REQ-RAG-039).</summary>
    private static List<object> BuildContentBlocks(ChatMessage message)
    {
        var blocks = new List<object>();

        if (message.Parts is { Count: > 0 })
        {
            foreach (var part in message.Parts)
            {
                if (part.Kind == ChatContentKind.Image && part.Image is not null)
                {
                    blocks.Add(new Dictionary<string, object>
                    {
                        ["type"] = "image",
                        ["source"] = part.Image.IsInline
                            ? new Dictionary<string, object>
                            {
                                ["type"] = "base64",
                                ["media_type"] = part.Image.MediaType,
                                ["data"] = part.Image.Base64Data!
                            }
                            : new Dictionary<string, object>
                            {
                                ["type"] = "url",
                                ["url"] = part.Image.Url!.ToString()
                            }
                    });
                }
                else if (!string.IsNullOrEmpty(part.Text))
                {
                    blocks.Add(new Dictionary<string, object> { ["type"] = "text", ["text"] = part.Text });
                }
            }
        }
        else if (!string.IsNullOrEmpty(message.Content))
        {
            blocks.Add(new Dictionary<string, object> { ["type"] = "text", ["text"] = message.Content });
        }

        // The API rejects an empty content array. A message with neither text nor parts is a caller
        // bug, but failing it here with an empty text block beats a 400 with no line number.
        if (blocks.Count == 0)
        {
            blocks.Add(new Dictionary<string, object> { ["type"] = "text", ["text"] = string.Empty });
        }

        return blocks;
    }

    /// <summary>Marks the end of the cacheable prefix on the last block of a message (REQ-RAG-043).</summary>
    private static void ApplyCacheBoundary(List<object> blocks, ChatMessage message, PromptCacheOptions? cache)
    {
        if (cache is null || !message.CacheBoundary || blocks.Count == 0) return;

        if (blocks[^1] is Dictionary<string, object> lastBlock)
        {
            lastBlock["cache_control"] = BuildCacheControl(cache);
        }
    }

    /// <summary>Builds the Anthropic <c>cache_control</c> value for the requested lifetime (REQ-RAG-043).</summary>
    private static Dictionary<string, object> BuildCacheControl(PromptCacheOptions cache)
    {
        var control = new Dictionary<string, object> { ["type"] = "ephemeral" };

        // Anthropic offers two tiers, not a free-form duration. Anything an hour or longer takes the
        // 1h tier; everything shorter takes the default 5m tier, which is left unstated so that a
        // caller asking for the default does not depend on the extended-TTL beta being enabled.
        if (cache.Ttl is { } ttl && ttl >= TimeSpan.FromHours(1))
        {
            control["ttl"] = "1h";
        }

        return control;
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

    private void RaiseCompletionEvent(int inputTokens, int outputTokens, TimeSpan duration, bool isStreaming, bool involvedToolCalls, int cacheReadTokens = 0, int cacheWriteTokens = 0)
    {
        TechieRagTelemetry.RecordLlmCompletion(
            Name, ModelName, inputTokens, outputTokens, duration, isStreaming, cacheReadTokens, cacheWriteTokens);

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

        /// <summary>Prompt tokens written into the cache on this call (REQ-RAG-043).</summary>
        [JsonPropertyName("cache_creation_input_tokens")]
        public int CacheCreationInputTokens { get; set; }

        /// <summary>Prompt tokens served from the cache on this call (REQ-RAG-043).</summary>
        [JsonPropertyName("cache_read_input_tokens")]
        public int CacheReadInputTokens { get; set; }
    }
}
