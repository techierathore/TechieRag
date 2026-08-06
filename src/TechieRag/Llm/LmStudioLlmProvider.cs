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
/// LLM provider implementation for LM Studio local model server.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enables LLM interactions using locally-hosted models via LM Studio,
/// using the OpenAI-compatible API at /v1/chat/completions.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when LlmSource.LmStudio is configured.</para>
/// </remarks>
public class LmStudioLlmProvider : ILlmProvider, IMultimodalLlmProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<LmStudioLlmProvider> logger;

    /// <inheritdoc/>
    public string Name => "LM Studio";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    /// <remarks>Images are encoded as <c>image_url</c> content parts, inline as a data URI or by URL (REQ-RAG-039).</remarks>
    public bool SupportsInput(ChatContentKind kind) =>
        kind is ChatContentKind.Text or ChatContentKind.Image;

    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;

    /// <summary>
    /// Creates a new LM Studio LLM provider instance.
    /// </summary>
    /// <param name="endpoint">LM Studio API endpoint (e.g., http://localhost:1234).</param>
    /// <param name="model">Model name (optional, LM Studio auto-selects loaded model).</param>
    /// <param name="logger">Logger instance.</param>
    public LmStudioLlmProvider(string endpoint, string model = "default", ILogger<LmStudioLlmProvider>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);

        ModelName = model;
        this.logger = logger ?? NullLogger<LmStudioLlmProvider>.Instance;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    /// <summary>
    /// Creates a new LM Studio LLM provider instance using a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>Test seam: allows a stubbed <see cref="HttpMessageHandler"/> to intercept requests.</remarks>
    /// <param name="httpClient">Pre-configured HTTP client (BaseAddress must be set).</param>
    /// <param name="model">Model name (optional, LM Studio auto-selects loaded model).</param>
    /// <param name="logger">Logger instance.</param>
    internal LmStudioLlmProvider(HttpClient httpClient, string model = "default", ILogger<LmStudioLlmProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        ModelName = model;
        this.logger = logger ?? NullLogger<LmStudioLlmProvider>.Instance;
        this.httpClient = httpClient;
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
        var request = BuildOpenAIRequest(messages, options, stream: false);
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("/v1/chat/completions", content, cancellationToken).ConfigureAwait(false);
        LlmHttpGuard.EnsureSuccess(response);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<OpenAIChatResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to parse LM Studio response");

        sw.Stop();

        var choice = result.Choices?.FirstOrDefault();
        var inputTokens = result.Usage?.PromptTokens ?? 0;
        var outputTokens = result.Usage?.CompletionTokens ?? 0;
        var cacheReadTokens = result.Usage?.CachedTokens ?? 0;
        var toolCalls = ParseToolCalls(choice?.Message?.ToolCalls);

        var llmResponse = new LlmResponse
        {
            Content = choice?.Message?.Content,
            ToolCalls = toolCalls,
            Usage = new TokenUsage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CacheReadTokens = cacheReadTokens,
                ModelName = ModelName,
                ProviderName = Name
            },
            FinishReason = choice?.FinishReason ?? "stop",
            ModelName = result.Model ?? ModelName
        };

        RaiseCompletionEvent(inputTokens, outputTokens, sw.Elapsed, false, llmResponse.HasToolCalls, cacheReadTokens);
        return llmResponse;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var request = BuildOpenAIRequest(messages, options, stream: true);
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions") { Content = content };
        var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        LlmHttpGuard.EnsureSuccess(response);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var outputText = new StringBuilder();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            var chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (chunk?.Usage is not null)
            {
                totalInputTokens = chunk.Usage.PromptTokens;
                totalOutputTokens = chunk.Usage.CompletionTokens;
            }

            var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(delta))
            {
                outputText.Append(delta);
                yield return delta;
            }
        }

        sw.Stop();

        // Fallback: estimate when the server sent no usage chunk
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

    private object BuildOpenAIRequest(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options, bool stream)
    {
        var apiMessages = messages.Select(m =>
        {
            var msg = new Dictionary<string, object> { ["role"] = m.Role };
            if (m.Content is not null || m.Parts is { Count: > 0 })
            {
                msg["content"] = OpenAIMessageMapper.BuildContent(m);
            }
            if (m.ToolCallId is not null) msg["tool_call_id"] = m.ToolCallId;
            if (m.ToolCalls is { Count: > 0 })
            {
                msg["tool_calls"] = m.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.ArgumentsJson }
                }).ToList();
            }
            return msg;
        }).ToList();

        var request = new Dictionary<string, object>
        {
            ["model"] = options?.Model ?? ModelName,
            ["messages"] = apiMessages,
            ["stream"] = stream
        };

        if (stream)
        {
            // Ask the server to append a final usage chunk to the stream (TR-RAG-002)
            request["stream_options"] = new Dictionary<string, object> { ["include_usage"] = true };
        }

        // REQ-RAG-043: prefix caching on these services is automatic; only the routing key is
        // expressible on the wire. See PromptCacheOptions for what is intentionally not sent.
        OpenAIMessageMapper.ApplyPromptCache(request, options?.PromptCache);

        if (options?.Temperature is not null) request["temperature"] = options.Temperature.Value;
        if (options?.MaxTokens is not null) request["max_tokens"] = options.MaxTokens.Value;
        if (options?.TopP is not null) request["top_p"] = options.TopP.Value;

        if (options?.Tools is { Count: > 0 })
        {
            request["tools"] = options.Tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersSchema)
                }
            }).ToList();

            if (options.ToolChoice is not null)
                request["tool_choice"] = options.ToolChoice;
        }

        return request;
    }

    private static List<ToolCall>? ParseToolCalls(List<OpenAIToolCall>? apiToolCalls)
    {
        if (apiToolCalls is not { Count: > 0 }) return null;

        return apiToolCalls.Select(tc => new ToolCall
        {
            Id = tc.Id ?? Guid.NewGuid().ToString(),
            Name = tc.Function?.Name ?? string.Empty,
            ArgumentsJson = tc.Function?.Arguments ?? "{}"
        }).ToList();
    }

    private void RaiseCompletionEvent(int inputTokens, int outputTokens, TimeSpan duration, bool isStreaming, bool involvedToolCalls, int cacheReadTokens = 0)
    {
        TechieRagTelemetry.RecordLlmCompletion(
            Name, ModelName, inputTokens, outputTokens, duration, isStreaming, cacheReadTokens);

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
}

// Shared OpenAI-format response DTOs used by LM Studio and OpenAI-Compatible providers
internal class OpenAIChatResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenAIChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public OpenAIUsage? Usage { get; set; }
}

internal class OpenAIChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public OpenAIMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class OpenAIMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAIToolCall>? ToolCalls { get; set; }
}

internal class OpenAIToolCall
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    public OpenAIFunctionCall? Function { get; set; }
}

internal class OpenAIFunctionCall
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

internal class OpenAIUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    /// <summary>Cache breakdown of the prompt tokens, where the service reports one (REQ-RAG-043).</summary>
    [JsonPropertyName("prompt_tokens_details")]
    public OpenAIPromptTokensDetails? PromptTokensDetails { get; set; }

    /// <summary>Prompt tokens served from the service's automatic prefix cache; zero when unreported.</summary>
    /// <remarks>These are counted inside <c>prompt_tokens</c>, not on top of it, so they must not be
    /// added to the input total — only reported alongside it.</remarks>
    public int CachedTokens => PromptTokensDetails?.CachedTokens ?? 0;
}

internal class OpenAIPromptTokensDetails
{
    [JsonPropertyName("cached_tokens")]
    public int CachedTokens { get; set; }
}

internal class OpenAIStreamChunk
{
    [JsonPropertyName("choices")]
    public List<OpenAIStreamChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public OpenAIUsage? Usage { get; set; }
}

internal class OpenAIStreamChoice
{
    [JsonPropertyName("delta")]
    public OpenAIStreamDelta? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class OpenAIStreamDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAIToolCall>? ToolCalls { get; set; }
}
