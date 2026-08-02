using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using TechieRag.Models;
using TechieRag.Speech;
using Xunit;

namespace TechieRag.Tests.Speech;

/// <summary>
/// Unit tests for the OpenAI-compatible speech providers (REQ-RAG-041 / BRD-122).
/// </summary>
public class OpenAICompatibleSpeechProviderTests
{
    /// <summary>Verifies a verbose transcription response yields text, language and duration.</summary>
    [Fact]
    public async Task TranscribeReadsTextLanguageAndDuration()
    {
        var handler = new SpeechStubHandler(VerboseTranscriptJson(), "application/json");
        var provider = BuildSpeechToText(handler);

        var transcript = await provider.TranscribeAsync(AudioStream(), "standup.mp3");

        Assert.Equal("Deploy is green. Ship it.", transcript.Text);
        Assert.Equal("en", transcript.Language);
        Assert.Equal(TimeSpan.FromSeconds(7.5), transcript.Duration);
    }

    /// <summary>Verifies segment timings survive the parse in wire order.</summary>
    [Fact]
    public async Task TranscribeReadsSegmentTimings()
    {
        var handler = new SpeechStubHandler(VerboseTranscriptJson(), "application/json");
        var provider = BuildSpeechToText(handler);

        var transcript = await provider.TranscribeAsync(AudioStream(), "standup.mp3");

        Assert.Equal(2, transcript.Segments.Count);
        Assert.Equal(0, transcript.Segments[0].Index);
        Assert.Equal("Deploy is green.", transcript.Segments[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(0.5), transcript.Segments[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(3.25), transcript.Segments[0].End);
        Assert.Equal(1, transcript.Segments[1].Index);
        Assert.Equal(TimeSpan.FromSeconds(7.5), transcript.Segments[1].End);
    }

    /// <summary>Verifies segment-free requests ask for the cheaper json response format.</summary>
    [Fact]
    public async Task TranscribeRequestsPlainJsonWhenSegmentsNotWanted()
    {
        var handler = new SpeechStubHandler("{\"text\":\"hello\"}", "application/json");
        var provider = BuildSpeechToText(handler);

        var transcript = await provider.TranscribeAsync(
            AudioStream(), "note.wav", new SpeechRecognitionOptions { IncludeSegments = false });

        Assert.Equal("json", handler.Field("response_format"));
        Assert.Empty(transcript.Segments);
    }

    /// <summary>Verifies the model, language and prompt reach the multipart body.</summary>
    [Fact]
    public async Task TranscribeSendsModelLanguageAndPrompt()
    {
        var handler = new SpeechStubHandler(VerboseTranscriptJson(), "application/json");
        var provider = BuildSpeechToText(handler);

        await provider.TranscribeAsync(
            AudioStream(),
            "standup.mp3",
            new SpeechRecognitionOptions { Language = "en", Prompt = "TechieRag, Qdrant" });

        Assert.Equal("whisper-1", handler.Field("model"));
        Assert.Equal("en", handler.Field("language"));
        Assert.Equal("TechieRag, Qdrant", handler.Field("prompt"));
        Assert.Equal("verbose_json", handler.Field("response_format"));
        Assert.Equal("/v1/audio/transcriptions", handler.Path);
    }

    /// <summary>Verifies a non-audio extension is rejected before any request is made.</summary>
    [Fact]
    public async Task TranscribeRejectsNonAudioExtension()
    {
        var handler = new SpeechStubHandler(VerboseTranscriptJson(), "application/json");
        var provider = BuildSpeechToText(handler);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.TranscribeAsync(AudioStream(), "notes.pdf"));
        Assert.Null(handler.Path);
    }

    /// <summary>Verifies synthesis returns the audio bytes with the right media type.</summary>
    [Fact]
    public async Task SynthesizeReturnsAudioBytesAndContentType()
    {
        var handler = new SpeechStubHandler("ID3-fake-mp3", "audio/mpeg");
        var provider = new OpenAICompatibleTextToSpeech(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234") }, "tts-1");

        var audio = await provider.SynthesizeAsync("Hello there.");

        Assert.Equal("mp3", audio.Format);
        Assert.Equal("audio/mpeg", audio.ContentType);
        Assert.Equal(Encoding.UTF8.GetBytes("ID3-fake-mp3"), audio.Data);
        Assert.Equal("/v1/audio/speech", handler.Path);
    }

    /// <summary>Verifies voice, format and speaking rate reach the request body.</summary>
    [Fact]
    public async Task SynthesizeSendsVoiceFormatAndRate()
    {
        var handler = new SpeechStubHandler("wav-bytes", "audio/wav");
        var provider = new OpenAICompatibleTextToSpeech(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234") }, "tts-1");

        var audio = await provider.SynthesizeAsync(
            "Hello there.",
            new SpeechSynthesisOptions { VoiceId = "nova", Format = "wav", SpeakingRate = 1.25 });

        Assert.Contains("\"voice\":\"nova\"", handler.Body);
        Assert.Contains("\"response_format\":\"wav\"", handler.Body);
        Assert.Contains("\"speed\":1.25", handler.Body);
        Assert.Equal("audio/wav", audio.ContentType);
    }

    /// <summary>Verifies an unsupported output format is rejected before any request is made.</summary>
    [Fact]
    public async Task SynthesizeRejectsUnsupportedFormat()
    {
        var handler = new SpeechStubHandler("bytes", "audio/mpeg");
        var provider = new OpenAICompatibleTextToSpeech(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234") }, "tts-1");

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.SynthesizeAsync("Hi", new SpeechSynthesisOptions { Format = "midi" }));
        Assert.Null(handler.Path);
    }

    /// <summary>Verifies a caller-supplied voice catalogue replaces the built-in one.</summary>
    [Fact]
    public async Task GetVoicesReturnsCallerSuppliedCatalogue()
    {
        var handler = new SpeechStubHandler("bytes", "audio/mpeg");
        var provider = new OpenAICompatibleTextToSpeech(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234") },
            "kokoro",
            [new SpeechVoice { Id = "af_bella", Name = "Bella", Language = "en" }]);

        var voices = await provider.GetVoicesAsync();

        Assert.Single(voices);
        Assert.Equal("af_bella", voices[0].Id);
    }

    /// <summary>Verifies a failing endpoint surfaces as an HTTP error rather than an empty transcript.</summary>
    [Fact]
    public async Task TranscribeThrowsOnEndpointFailure()
    {
        var handler = new SpeechStubHandler("nope", "text/plain", HttpStatusCode.InternalServerError);
        var provider = BuildSpeechToText(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.TranscribeAsync(AudioStream(), "standup.mp3"));
    }

    /// <summary>Builds a speech-to-text provider over the stub handler.</summary>
    /// <param name="handler">The stub handler to intercept requests.</param>
    /// <returns>The provider under test.</returns>
    private static OpenAICompatibleSpeechToText BuildSpeechToText(SpeechStubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234") }, "whisper-1");

    /// <summary>Builds a throwaway audio payload.</summary>
    /// <returns>A stream standing in for encoded audio.</returns>
    private static MemoryStream AudioStream() => new(Encoding.UTF8.GetBytes("fake-audio-bytes"));

    /// <summary>Builds a verbose_json transcription response with two segments.</summary>
    /// <returns>The response JSON.</returns>
    private static string VerboseTranscriptJson() =>
        """
        {
          "task": "transcribe",
          "language": "en",
          "duration": 7.5,
          "text": "Deploy is green. Ship it.",
          "segments": [
            { "id": 0, "start": 0.5, "end": 3.25, "text": " Deploy is green." },
            { "id": 1, "start": 3.25, "end": 7.5, "text": " Ship it." }
          ]
        }
        """;
}

/// <summary>
/// Captures one outgoing speech request and answers with a canned body.
/// </summary>
internal sealed class SpeechStubHandler : HttpMessageHandler
{
    private readonly string responseBody;
    private readonly string contentType;
    private readonly HttpStatusCode status;

    /// <summary>Creates the handler.</summary>
    /// <param name="responseBody">The body to answer with.</param>
    /// <param name="contentType">The response media type.</param>
    /// <param name="status">The response status code.</param>
    public SpeechStubHandler(string responseBody, string contentType, HttpStatusCode status = HttpStatusCode.OK)
    {
        this.responseBody = responseBody;
        this.contentType = contentType;
        this.status = status;
    }

    /// <summary>Gets the request path seen, or null when no request was made.</summary>
    public string? Path { get; private set; }

    /// <summary>Gets the request body seen.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>
    /// Reads one multipart form field out of the captured body.
    /// </summary>
    /// <param name="name">The field name.</param>
    /// <returns>The field value, or null when the request carried no such field.</returns>
    /// <remarks>
    /// The name is matched with or without surrounding quotes: .NET writes
    /// <c>name=model</c> where the RFC permits <c>name="model"</c>, and a test that assumed either
    /// form would be asserting on a serializer detail rather than on the wire contract.
    /// </remarks>
    public string? Field(string name)
    {
        var match = Regex.Match(
            Body,
            $"name=\"?{Regex.Escape(name)}\"?\r\n\r\n(.*?)\r\n--",
            RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Path = request.RequestUri!.AbsolutePath;
        if (request.Content is not null)
        {
            Body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, contentType)
        };
    }
}
