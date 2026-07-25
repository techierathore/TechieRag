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
/// LLM provider implementation for Google Gemini API.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enables LLM interactions using Google's Gemini models
/// via the Generative Language API.</para>
/// <para><b>API Differences:</b> Gemini uses "parts" instead of "content" in messages,
/// role names differ ("model" instead of "assistant"), and tool definitions use
/// a different schema format ("functionDeclarations").</para>
/// </remarks>
public class GoogleGeminiLlmProvider : ILlmProvider
{
    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string baseUrl;
    private readonly ILogger<GoogleGeminiLlmProvider> logger;

    /// <inheritdoc/>
    public string Name => "Google Gemini";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;

    /// <summary>
    /// Creates a new Google Gemini LLM provider instance.
    /// </summary>
    /// <param name="apiKey">Google AI API key.</param>
    /// <param name="model">Model name (e.g., "gemini-2.0-flash").</param>
    /// <param name="endpoint">API endpoint (defaults to Google's generative language API).</param>
    /// <param name="logger">Logger instance.</param>
    public GoogleGeminiLlmProvider(string apiKey, string model = "gemini-2.0-flash", string? endpoint = null, ILogger<GoogleGeminiLlmProvider>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);

        this.apiKey = apiKey;
        ModelName = model;
        baseUrl = (endpoint ?? "https://generativelanguage.googleapis.com").TrimEnd('/');
        this.logger = logger ?? NullLogger<GoogleGeminiLlmProvider>.Instance;
        httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    /// <summary>
    /// Creates a Google Gemini provider with a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>Test seam: allows a stubbed <see cref="HttpMessageHandler"/> to intercept requests.</remarks>
    /// <param name="httpClient">Pre-configured HTTP client.</param>
    /// <param name="apiKey">Google AI API key.</param>
    /// <param name="model">Model name.</param>
    /// <param name="logger">Logger instance.</param>
    internal GoogleGeminiLlmProvider(HttpClient httpClient, string apiKey, string model, ILogger<GoogleGeminiLlmProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        this.apiKey = apiKey;
        ModelName = model;
        baseUrl = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "https://generativelanguage.googleapis.com";
        this.logger = logger ?? NullLogger<GoogleGeminiLlmProvider>.Instance;
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
        var url = $"{baseUrl}/v1beta/models/{options?.Model ?? ModelName}:generateContent?key={apiKey}";
        var request = BuildGeminiRequest(messages, options);
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        LlmHttpGuard.EnsureSuccess(response);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to parse Gemini response");

        sw.Stop();

        var candidate = result.Candidates?.FirstOrDefault();
        var textContent = ExtractText(candidate);
        var toolCalls = ExtractToolCalls(candidate);
        var inputTokens = result.UsageMetadata?.PromptTokenCount ?? 0;
        var outputTokens = result.UsageMetadata?.CandidatesTokenCount ?? 0;

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
        var url = $"{baseUrl}/v1beta/models/{options?.Model ?? ModelName}:streamGenerateContent?key={apiKey}&alt=sse";
        var request = BuildGeminiRequest(messages, options);
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
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
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            var chunk = JsonSerializer.Deserialize<GeminiResponse>(data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (chunk is null) continue;

            if (chunk.UsageMetadata is not null)
            {
                totalInputTokens = chunk.UsageMetadata.PromptTokenCount;
                totalOutputTokens = chunk.UsageMetadata.CandidatesTokenCount;
            }

            var text = ExtractText(chunk.Candidates?.FirstOrDefault());
            if (!string.IsNullOrEmpty(text))
            {
                outputText.Append(text);
                yield return text;
            }
        }

        sw.Stop();

        // Fallback: estimate when the API sent no usage metadata
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

    private object BuildGeminiRequest(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options)
    {
        var request = new Dictionary<string, object>();

        // Extract system instruction
        var systemMsg = messages.FirstOrDefault(m => m.Role == "system");
        if (systemMsg is not null)
        {
            request["systemInstruction"] = new { parts = new[] { new { text = systemMsg.Content ?? string.Empty } } };
        }

        // Map messages (skip system messages)
        var contents = messages
            .Where(m => m.Role != "system")
            .Select(m => new
            {
                role = m.Role == "assistant" ? "model" : m.Role,
                parts = new[] { new { text = m.Content ?? string.Empty } }
            }).ToList();

        request["contents"] = contents;

        // Generation config
        var genConfig = new Dictionary<string, object>();
        if (options?.Temperature is not null) genConfig["temperature"] = options.Temperature.Value;
        if (options?.MaxTokens is not null) genConfig["maxOutputTokens"] = options.MaxTokens.Value;
        if (options?.TopP is not null) genConfig["topP"] = options.TopP.Value;

        if (genConfig.Count > 0)
            request["generationConfig"] = genConfig;

        // Tool definitions
        if (options?.Tools is { Count: > 0 })
        {
            request["tools"] = new[]
            {
                new
                {
                    functionDeclarations = options.Tools.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersSchema)
                    }).ToList()
                }
            };
        }

        return request;
    }

    private static string? ExtractText(GeminiCandidate? candidate)
    {
        if (candidate?.Content?.Parts is null) return null;

        var textParts = candidate.Content.Parts
            .Where(p => !string.IsNullOrEmpty(p.Text))
            .Select(p => p.Text);

        var text = string.Join("", textParts);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static List<ToolCall>? ExtractToolCalls(GeminiCandidate? candidate)
    {
        if (candidate?.Content?.Parts is null) return null;

        var functionCalls = candidate.Content.Parts
            .Where(p => p.FunctionCall is not null)
            .Select(p => new ToolCall
            {
                Id = Guid.NewGuid().ToString(),
                Name = p.FunctionCall!.Name ?? string.Empty,
                ArgumentsJson = p.FunctionCall.Args is not null
                    ? JsonSerializer.Serialize(p.FunctionCall.Args)
                    : "{}"
            }).ToList();

        return functionCalls.Count > 0 ? functionCalls : null;
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

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }

        [JsonPropertyName("usageMetadata")]
        public GeminiUsageMetadata? UsageMetadata { get; set; }
    }

    private class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart>? Parts { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }
    }

    private class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("functionCall")]
        public GeminiFunctionCall? FunctionCall { get; set; }
    }

    private class GeminiFunctionCall
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("args")]
        public JsonElement? Args { get; set; }
    }

    private class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }
    }
}
