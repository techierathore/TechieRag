using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;

namespace TechieRag.Speech;

/// <summary>
/// Speech-to-text over the OpenAI-compatible <c>/v1/audio/transcriptions</c> endpoint (REQ-RAG-041).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives <see cref="ISpeechToText"/> a working implementation with no new
/// dependency — the request is a plain multipart POST over <see cref="HttpClient"/>.</para>
/// <para><b>Compatibility:</b> The same wire shape is served by OpenAI (<c>whisper-1</c>,
/// <c>gpt-4o-transcribe</c>), Groq, and any local host that mirrors it — a self-hosted
/// <c>whisper.cpp</c> server or LocalAI included. Pointing the endpoint at localhost keeps audio on
/// the machine, which is what makes this usable under a local-first product.</para>
/// <para><b>Credentials:</b> the API key is supplied by the caller, is attached only as a Bearer
/// header, and is never written to a log or an exception message.</para>
/// <para><b>Limitations:</b> Providers cap the upload size (25 MB on OpenAI at the time of writing).
/// Longer recordings must be split by the caller; this class does not chunk audio.</para>
/// </remarks>
public class OpenAICompatibleSpeechToText : ISpeechToText
{
    /// <summary>Audio containers the OpenAI transcription wire contract accepts.</summary>
    private static readonly string[] AudioExtensions =
        [".flac", ".m4a", ".mp3", ".mp4", ".mpeg", ".mpga", ".oga", ".ogg", ".wav", ".webm"];

    private readonly HttpClient httpClient;
    private readonly string transcriptionsPath;
    private readonly ILogger<OpenAICompatibleSpeechToText> logger;

    /// <inheritdoc/>
    public string Name => "OpenAI-Compatible";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> SupportedExtensions => AudioExtensions;

    /// <inheritdoc/>
    public bool SupportsSegments => true;

    /// <summary>
    /// Creates a provider bound to an OpenAI-compatible endpoint.
    /// </summary>
    /// <param name="endpoint">API endpoint (for example "https://api.openai.com/v1").</param>
    /// <param name="apiKey">API key for authentication; empty for a local host that needs none.</param>
    /// <param name="model">Transcription model name (for example "whisper-1").</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentException">Thrown when the endpoint or model is empty.</exception>
    public OpenAICompatibleSpeechToText(
        string endpoint,
        string apiKey,
        string model,
        ILogger<OpenAICompatibleSpeechToText>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(model);

        ModelName = model;
        this.logger = logger ?? NullLogger<OpenAICompatibleSpeechToText>.Instance;

        var baseUri = endpoint.TrimEnd('/');
        transcriptionsPath = baseUri.EndsWith("/v1", StringComparison.Ordinal)
            ? "/v1/audio/transcriptions"
            : "/audio/transcriptions";

        httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUri.EndsWith("/v1", StringComparison.Ordinal) ? baseUri[..^3] : baseUri),
            Timeout = TimeSpan.FromMinutes(5)
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
    /// <param name="model">Transcription model name.</param>
    /// <param name="logger">Logger instance.</param>
    internal OpenAICompatibleSpeechToText(
        HttpClient httpClient,
        string model,
        ILogger<OpenAICompatibleSpeechToText>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        ModelName = model;
        transcriptionsPath = "/v1/audio/transcriptions";
        this.logger = logger ?? NullLogger<OpenAICompatibleSpeechToText>.Instance;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when the audio stream or file name is null.</exception>
    /// <exception cref="NotSupportedException">Thrown when the file extension is not an audio format.</exception>
    public async Task<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string fileName,
        SpeechRecognitionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AudioExtensions.Contains(extension))
        {
            throw new NotSupportedException(
                $"'{extension}' is not an audio format this provider accepts. Supported: {string.Join(", ", AudioExtensions)}.");
        }

        options ??= new SpeechRecognitionOptions();

        using var form = BuildForm(audio, fileName, options);
        using var response = await httpClient
            .PostAsync(transcriptionsPath, form, cancellationToken)
            .ConfigureAwait(false);

        LlmHttpGuard.EnsureSuccess(response);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var transcript = ParseTranscript(json, options.IncludeSegments);

        logger.LogInformation(
            "Transcribed {FileName} with {Model}: {Characters} characters, {Segments} segment(s)",
            fileName, ModelName, transcript.Text.Length, transcript.Segments.Count);

        return transcript;
    }

    /// <summary>
    /// Builds the multipart body the transcription endpoint expects.
    /// </summary>
    /// <param name="audio">The audio content stream.</param>
    /// <param name="fileName">The original file name, used as the multipart file name.</param>
    /// <param name="options">The recognition options in effect.</param>
    /// <returns>The multipart form content; the caller disposes it.</returns>
    private MultipartFormDataContent BuildForm(
        Stream audio, string fileName, SpeechRecognitionOptions options)
    {
        var form = new MultipartFormDataContent();

        var file = new StreamContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", Path.GetFileName(fileName));
        form.Add(new StringContent(ModelName), "model");

        // verbose_json is the only response format that carries segment timings. Asking for it only
        // when segments are wanted keeps the plain-text path cheap for hosts that do not implement it.
        form.Add(new StringContent(options.IncludeSegments ? "verbose_json" : "json"), "response_format");

        if (!string.IsNullOrWhiteSpace(options.Language))
        {
            form.Add(new StringContent(options.Language), "language");
        }

        if (!string.IsNullOrWhiteSpace(options.Prompt))
        {
            form.Add(new StringContent(options.Prompt), "prompt");
        }

        if (options.Temperature is not null)
        {
            form.Add(
                new StringContent(options.Temperature.Value.ToString("0.##", CultureInfo.InvariantCulture)),
                "temperature");
        }

        return form;
    }

    /// <summary>
    /// Parses a transcription response into a <see cref="SpeechTranscript"/>.
    /// </summary>
    /// <param name="json">The response body.</param>
    /// <param name="includeSegments">Whether segments were requested.</param>
    /// <returns>The parsed transcript; segments are empty when the response carried none.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the response has no text field.</exception>
    private static SpeechTranscript ParseTranscript(string json, bool includeSegments)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("text", out var textElement))
        {
            throw new InvalidOperationException(
                "The transcription endpoint returned a response with no 'text' field.");
        }

        var text = textElement.GetString() ?? string.Empty;

        string? language = root.TryGetProperty("language", out var languageElement)
            ? languageElement.GetString()
            : null;

        TimeSpan? duration = root.TryGetProperty("duration", out var durationElement)
            && durationElement.ValueKind is JsonValueKind.Number
            ? TimeSpan.FromSeconds(durationElement.GetDouble())
            : null;

        var segments = includeSegments ? ParseSegments(root) : [];

        return new SpeechTranscript
        {
            Text = text.Trim(),
            Language = language,
            Duration = duration,
            Segments = segments
        };
    }

    /// <summary>
    /// Reads the timestamped segment array out of a verbose transcription response.
    /// </summary>
    /// <param name="root">The response root element.</param>
    /// <returns>The segments in wire order; empty when the response carried none.</returns>
    private static IReadOnlyList<TranscriptSegment> ParseSegments(JsonElement root)
    {
        if (!root.TryGetProperty("segments", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var segments = new List<TranscriptSegment>();
        var index = 0;

        foreach (var element in array.EnumerateArray())
        {
            var segmentText = element.TryGetProperty("text", out var textElement)
                ? textElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;

            if (segmentText.Length == 0)
            {
                continue;
            }

            segments.Add(new TranscriptSegment
            {
                Index = index++,
                Start = ReadSeconds(element, "start"),
                End = ReadSeconds(element, "end"),
                Text = segmentText
            });
        }

        return segments;
    }

    /// <summary>
    /// Reads a numeric seconds property as a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="element">The segment element.</param>
    /// <param name="property">The property name to read.</param>
    /// <returns>The offset, or <see cref="TimeSpan.Zero"/> when absent or non-numeric.</returns>
    private static TimeSpan ReadSeconds(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromSeconds(value.GetDouble())
            : TimeSpan.Zero;
    }
}
