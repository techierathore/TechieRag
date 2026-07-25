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
/// LLM provider implementation for Ollama local model server.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enables LLM interactions using locally-hosted models via Ollama,
/// supporting chat completions, streaming, and tool calling (Ollama v0.4+).</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when LlmSource.Ollama is configured.
/// Communicates with Ollama's HTTP API at /api/chat endpoint.</para>
/// </remarks>
public class OllamaLlmProvider : ILlmProvider
{
    private readonly HttpClient httpClient;
    private readonly string endpoint;
    private readonly ILogger<OllamaLlmProvider> logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc/>
    public string Name => "Ollama";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;

    /// <summary>
    /// Creates a new Ollama LLM provider instance.
    /// </summary>
    /// <param name="endpoint">Ollama API endpoint (e.g., http://localhost:11434).</param>
    /// <param name="model">Model name to use (e.g., "llama3.2", "mistral").</param>
    /// <param name="logger">Logger instance.</param>
    public OllamaLlmProvider(string endpoint, string model, ILogger<OllamaLlmProvider>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(model);

        this.endpoint = endpoint.TrimEnd('/');
        ModelName = model;
        this.logger = logger ?? NullLogger<OllamaLlmProvider>.Instance;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(this.endpoint),
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    /// <summary>
    /// Creates an Ollama provider with a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>Test seam: allows a stubbed <see cref="HttpMessageHandler"/> to intercept requests.</remarks>
    /// <param name="httpClient">Pre-configured HTTP client (BaseAddress must be set).</param>
    /// <param name="model">Model name to use.</param>
    /// <param name="logger">Logger instance.</param>
    internal OllamaLlmProvider(HttpClient httpClient, string model, ILogger<OllamaLlmProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        endpoint = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "http://localhost:11434";
        ModelName = model;
        this.logger = logger ?? NullLogger<OllamaLlmProvider>.Instance;
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
        var request = BuildRequest(messages, options, stream: false);
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("/api/chat", content, cancellationToken).ConfigureAwait(false);
        LlmHttpGuard.EnsureSuccess(response);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var ollamaResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse Ollama response");

        sw.Stop();

        var inputTokens = ollamaResponse.PromptEvalCount ?? 0;
        var outputTokens = ollamaResponse.EvalCount ?? 0;

        var toolCalls = ParseToolCalls(ollamaResponse.Message?.ToolCalls);

        var llmResponse = new LlmResponse
        {
            Content = ollamaResponse.Message?.Content,
            ToolCalls = toolCalls,
            Usage = new TokenUsage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ModelName = ModelName,
                ProviderName = Name
            },
            FinishReason = toolCalls is { Count: > 0 } ? "tool_calls" : "stop",
            ModelName = ModelName
        };

        RaiseCompletionEvent(inputTokens, outputTokens, sw.Elapsed, false, llmResponse.HasToolCalls);
        return llmResponse;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var request = BuildRequest(messages, options, stream: true);
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = content };
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

            var chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line, JsonOptions);
            if (chunk is null) continue;

            if (chunk.Done == true)
            {
                totalInputTokens = chunk.PromptEvalCount ?? totalInputTokens;
                totalOutputTokens = chunk.EvalCount ?? totalOutputTokens;
                break;
            }

            if (!string.IsNullOrEmpty(chunk.Message?.Content))
            {
                outputText.Append(chunk.Message.Content);
                yield return chunk.Message.Content;
            }
        }

        sw.Stop();

        // Fallback: estimate when the server sent no eval counts
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
        options ??= new LlmCompletionOptions();
        var jsonPrompt = $"{prompt}\n\nRespond with valid JSON only. The response must conform to this structure: {typeof(T).Name}";

        var response = await CompleteAsync(jsonPrompt, new LlmCompletionOptions
        {
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens,
            SystemPrompt = options.SystemPrompt ?? "You are a helpful assistant that responds only with valid JSON.",
            JsonMode = true
        }, cancellationToken).ConfigureAwait(false);

        var content = response.Content?.Trim() ?? throw new InvalidOperationException("LLM returned empty response");

        // Strip markdown code fences if present
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

    private object BuildRequest(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options, bool stream)
    {
        var ollamaMessages = messages.Select(m => new
        {
            role = m.Role,
            content = m.Content ?? string.Empty,
            tool_call_id = m.ToolCallId
        }).ToList();

        var request = new Dictionary<string, object>
        {
            ["model"] = options?.Model ?? ModelName,
            ["messages"] = ollamaMessages,
            ["stream"] = stream
        };

        var ollamaOptions = new Dictionary<string, object>();
        if (options?.Temperature is not null) ollamaOptions["temperature"] = options.Temperature.Value;
        if (options?.MaxTokens is not null) ollamaOptions["num_predict"] = options.MaxTokens.Value;
        if (options?.TopP is not null) ollamaOptions["top_p"] = options.TopP.Value;
        if (options?.Seed is not null) ollamaOptions["seed"] = options.Seed.Value;

        if (ollamaOptions.Count > 0)
            request["options"] = ollamaOptions;

        if (options?.JsonMode == true)
            request["format"] = "json";

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
        }

        return request;
    }

    private static List<ToolCall>? ParseToolCalls(List<OllamaToolCall>? ollamaToolCalls)
    {
        if (ollamaToolCalls is not { Count: > 0 }) return null;

        return ollamaToolCalls.Select(tc => new ToolCall
        {
            Id = Guid.NewGuid().ToString(),
            Name = tc.Function?.Name ?? string.Empty,
            ArgumentsJson = tc.Function?.Arguments is not null
                ? JsonSerializer.Serialize(tc.Function.Arguments)
                : "{}"
        }).ToList();
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

    private class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }

        [JsonPropertyName("done")]
        public bool? Done { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; set; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; set; }
    }

    private class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("tool_calls")]
        public List<OllamaToolCall>? ToolCalls { get; set; }
    }

    private class OllamaToolCall
    {
        [JsonPropertyName("function")]
        public OllamaToolCallFunction? Function { get; set; }
    }

    private class OllamaToolCallFunction
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public JsonElement? Arguments { get; set; }
    }
}
