using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;

namespace TechieRag.Speech;

/// <summary>
/// Text-to-speech over the OpenAI-compatible <c>/v1/audio/speech</c> endpoint (REQ-RAG-041).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives <see cref="ITextToSpeech"/> a working implementation with no new
/// dependency — a JSON POST over <see cref="HttpClient"/> returning encoded audio bytes.</para>
/// <para><b>Compatibility:</b> OpenAI's speech endpoint and the local hosts that mirror it
/// (LocalAI, and Piper/Kokoro servers exposing the same route).</para>
/// <para><b>Credentials:</b> the API key is supplied by the caller, attached only as a Bearer
/// header, and never written to a log or an exception message.</para>
/// <para><b>Voices:</b> the wire contract has no list-voices route, so
/// <see cref="GetVoicesAsync"/> returns the documented OpenAI catalogue or a caller-supplied list.
/// A host pointing at a different server should pass its own voices rather than trust the default.
/// </para>
/// </remarks>
public class OpenAICompatibleTextToSpeech : ITextToSpeech
{
    /// <summary>Formats the OpenAI speech wire contract can emit.</summary>
    private static readonly string[] AudioFormats = ["mp3", "opus", "aac", "flac", "wav", "pcm"];

    /// <summary>
    /// OpenAI's documented voice catalogue, used when the caller supplies no voice list.
    /// </summary>
    private static readonly SpeechVoice[] DefaultVoices =
    [
        new() { Id = "alloy", Name = "Alloy" },
        new() { Id = "echo", Name = "Echo" },
        new() { Id = "fable", Name = "Fable" },
        new() { Id = "onyx", Name = "Onyx" },
        new() { Id = "nova", Name = "Nova" },
        new() { Id = "shimmer", Name = "Shimmer" }
    ];

    private readonly HttpClient httpClient;
    private readonly string speechPath;
    private readonly IReadOnlyList<SpeechVoice> voices;
    private readonly ILogger<OpenAICompatibleTextToSpeech> logger;

    /// <inheritdoc/>
    public string Name => "OpenAI-Compatible";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> SupportedFormats => AudioFormats;

    /// <summary>
    /// Creates a provider bound to an OpenAI-compatible endpoint.
    /// </summary>
    /// <param name="endpoint">API endpoint (for example "https://api.openai.com/v1").</param>
    /// <param name="apiKey">API key for authentication; empty for a local host that needs none.</param>
    /// <param name="model">Synthesis model name (for example "tts-1").</param>
    /// <param name="voices">Voices this endpoint offers; null uses the OpenAI catalogue.</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentException">Thrown when the endpoint or model is empty.</exception>
    public OpenAICompatibleTextToSpeech(
        string endpoint,
        string apiKey,
        string model,
        IReadOnlyList<SpeechVoice>? voices = null,
        ILogger<OpenAICompatibleTextToSpeech>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(model);

        ModelName = model;
        this.voices = voices ?? DefaultVoices;
        this.logger = logger ?? NullLogger<OpenAICompatibleTextToSpeech>.Instance;

        var baseUri = endpoint.TrimEnd('/');
        speechPath = baseUri.EndsWith("/v1", StringComparison.Ordinal)
            ? "/v1/audio/speech"
            : "/audio/speech";

        httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUri.EndsWith("/v1", StringComparison.Ordinal) ? baseUri[..^3] : baseUri),
            Timeout = TimeSpan.FromMinutes(2)
        };

        if (!string.IsNullOrEmpty(apiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    /// <summary>
    /// Creates a provider over a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>Test seam: allows a stubbed <see cref="HttpMessageHandler"/> to intercept requests.</remarks>
    /// <param name="httpClient">Pre-configured HTTP client with a base address set.</param>
    /// <param name="model">Synthesis model name.</param>
    /// <param name="voices">Voices this endpoint offers; null uses the OpenAI catalogue.</param>
    /// <param name="logger">Logger instance.</param>
    internal OpenAICompatibleTextToSpeech(
        HttpClient httpClient,
        string model,
        IReadOnlyList<SpeechVoice>? voices = null,
        ILogger<OpenAICompatibleTextToSpeech>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        ModelName = model;
        speechPath = "/v1/audio/speech";
        this.voices = voices ?? DefaultVoices;
        this.logger = logger ?? NullLogger<OpenAICompatibleTextToSpeech>.Instance;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">Thrown when the text is empty.</exception>
    /// <exception cref="NotSupportedException">Thrown when an unsupported format is requested.</exception>
    public async Task<SpeechAudio> SynthesizeAsync(
        string text,
        SpeechSynthesisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        options ??= new SpeechSynthesisOptions();
        var format = (options.Format ?? "mp3").ToLowerInvariant();
        if (!AudioFormats.Contains(format))
        {
            throw new NotSupportedException(
                $"'{format}' is not a format this provider emits. Supported: {string.Join(", ", AudioFormats)}.");
        }

        var payload = BuildPayload(text, options, format);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await httpClient
            .PostAsync(speechPath, content, cancellationToken)
            .ConfigureAwait(false);

        LlmHttpGuard.EnsureSuccess(response);

        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Synthesized {Characters} character(s) with {Model} as {Format}: {Bytes} byte(s)",
            text.Length, ModelName, format, data.Length);

        return new SpeechAudio
        {
            Data = data,
            Format = format,
            ContentType = ContentTypeFor(format)
        };
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(voices);
    }

    /// <summary>
    /// Builds the JSON request body for the speech endpoint.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="options">The synthesis options in effect.</param>
    /// <param name="format">The resolved output format.</param>
    /// <returns>The serialized request body.</returns>
    private string BuildPayload(string text, SpeechSynthesisOptions options, string format)
    {
        var request = new Dictionary<string, object>
        {
            ["model"] = ModelName,
            ["input"] = text,
            ["voice"] = options.VoiceId ?? voices.FirstOrDefault()?.Id ?? "alloy",
            ["response_format"] = format
        };

        if (options.SpeakingRate is not null)
        {
            request["speed"] = Math.Round(options.SpeakingRate.Value, 2);
        }

        if (!string.IsNullOrWhiteSpace(options.Language))
        {
            request["language"] = options.Language;
        }

        return JsonSerializer.Serialize(request);
    }

    /// <summary>
    /// Maps an audio format name to its IANA media type.
    /// </summary>
    /// <param name="format">The format name.</param>
    /// <returns>The media type for that format.</returns>
    private static string ContentTypeFor(string format) => format switch
    {
        "mp3" => "audio/mpeg",
        "opus" => "audio/opus",
        "aac" => "audio/aac",
        "flac" => "audio/flac",
        "wav" => "audio/wav",
        "pcm" => "audio/pcm",
        _ => "application/octet-stream"
    };
}
