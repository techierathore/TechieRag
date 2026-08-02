using System.Text;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Processors;
using Xunit;

namespace TechieRag.Tests.Processors;

/// <summary>
/// Unit tests for <see cref="AudioTranscriptionProcessor"/> (REQ-RAG-040 / BRD-121).
/// </summary>
public class AudioTranscriptionProcessorTests
{
    /// <summary>Verifies the processor claims exactly the formats its provider accepts.</summary>
    [Fact]
    public void SupportedExtensionsComeFromTheProvider()
    {
        var processor = new AudioTranscriptionProcessor(new FakeSpeechToText(Transcript()));

        Assert.Equal(new[] { ".mp3", ".wav" }, processor.SupportedExtensions);
    }

    /// <summary>Verifies consecutive segments are packed into one chunk while they fit.</summary>
    [Fact]
    public async Task SegmentsArePackedIntoChunksUntilTheSizeLimit()
    {
        var processor = new AudioTranscriptionProcessor(new FakeSpeechToText(Transcript()));

        var chunks = await processor.ProcessAsync(
            AudioStream(), "standup.mp3", new DocumentProcessingOptions { MaxChunkSize = 500 });

        Assert.Single(chunks);
        Assert.Equal("Deploy is green. Ship it. Retro at four.", chunks[0].Text);
    }

    /// <summary>Verifies a chunk is closed once the next segment would overflow the size limit.</summary>
    [Fact]
    public async Task SegmentsSplitIntoNewChunkWhenTheLimitWouldBeExceeded()
    {
        var processor = new AudioTranscriptionProcessor(new FakeSpeechToText(Transcript()));

        var chunks = await processor.ProcessAsync(
            AudioStream(), "standup.mp3", new DocumentProcessingOptions { MaxChunkSize = 20 });

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Deploy is green.", chunks[0].Text);
        Assert.Equal("Ship it.", chunks[1].Text);
        Assert.Equal("Retro at four.", chunks[2].Text);
        Assert.Equal(new int?[] { 0, 1, 2 }, chunks.Select(chunk => chunk.ChunkIndex));
    }

    /// <summary>Verifies each chunk carries the audio offsets that bound its own segments.</summary>
    [Fact]
    public async Task ChunksCarryTheirAudioOffsets()
    {
        var processor = new AudioTranscriptionProcessor(new FakeSpeechToText(Transcript()));

        var chunks = await processor.ProcessAsync(
            AudioStream(), "standup.mp3", new DocumentProcessingOptions { MaxChunkSize = 20 });

        Assert.Equal(0.5d, chunks[0].Metadata[AudioTranscriptionProcessor.StartSecondsKey]);
        Assert.Equal(3.25d, chunks[0].Metadata[AudioTranscriptionProcessor.EndSecondsKey]);
        Assert.Equal(3.25d, chunks[1].Metadata[AudioTranscriptionProcessor.StartSecondsKey]);
        Assert.Equal(12d, chunks[2].Metadata[AudioTranscriptionProcessor.EndSecondsKey]);
    }

    /// <summary>Verifies the transcript language and audio duration reach every chunk.</summary>
    [Fact]
    public async Task ChunksCarryLanguageAndDuration()
    {
        var processor = new AudioTranscriptionProcessor(new FakeSpeechToText(Transcript()));

        var chunks = await processor.ProcessAsync(AudioStream(), "standup.mp3");

        Assert.All(chunks, chunk =>
        {
            Assert.Equal("en", chunk.Metadata[AudioTranscriptionProcessor.LanguageKey]);
            Assert.Equal(12d, chunk.Metadata[AudioTranscriptionProcessor.DurationSecondsKey]);
            Assert.Equal("standup.mp3", chunk.Metadata["sourceFile"]);
            Assert.Equal("standup", chunk.DocumentId);
        });
    }

    /// <summary>
    /// Verifies a provider that returns no segments still ingests, via flat text chunking, and that
    /// those chunks carry no audio offsets to claim a position they do not have.
    /// </summary>
    [Fact]
    public async Task TranscriptWithoutSegmentsFallsBackToTextChunking()
    {
        var flat = new SpeechTranscript
        {
            Text = string.Join(" ", Enumerable.Repeat("word", 200)),
            Language = "en"
        };
        var processor = new AudioTranscriptionProcessor(new FakeSpeechToText(flat));

        var chunks = await processor.ProcessAsync(
            AudioStream(), "voicenote.wav", new DocumentProcessingOptions { MaxChunkSize = 120 });

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk =>
            Assert.False(chunk.Metadata.ContainsKey(AudioTranscriptionProcessor.StartSecondsKey)));
    }

    /// <summary>Verifies silence produces no chunks rather than one empty chunk.</summary>
    [Fact]
    public async Task SilentAudioProducesNoChunks()
    {
        var silence = new SpeechTranscript { Text = "   " };
        var processor = new AudioTranscriptionProcessor(new FakeSpeechToText(silence));

        var chunks = await processor.ProcessAsync(AudioStream(), "silence.wav");

        Assert.Empty(chunks);
    }

    /// <summary>Verifies the caller's language hint is passed through to the provider.</summary>
    [Fact]
    public async Task LanguageHintReachesTheProvider()
    {
        var provider = new FakeSpeechToText(Transcript());
        var processor = new AudioTranscriptionProcessor(provider);

        await processor.ProcessAsync(
            AudioStream(), "standup.mp3", new DocumentProcessingOptions { Language = "de" });

        Assert.Equal("de", provider.LastOptions?.Language);
        Assert.True(provider.LastOptions?.IncludeSegments);
    }

    /// <summary>Verifies caller metadata is merged onto every chunk.</summary>
    [Fact]
    public async Task CallerMetadataIsMergedOntoChunks()
    {
        var processor = new AudioTranscriptionProcessor(new FakeSpeechToText(Transcript()));

        var chunks = await processor.ProcessAsync(
            AudioStream(),
            "standup.mp3",
            new DocumentProcessingOptions { Metadata = new Dictionary<string, object> { ["workspaceId"] = "w1" } });

        Assert.All(chunks, chunk => Assert.Equal("w1", chunk.Metadata["workspaceId"]));
    }

    /// <summary>Verifies offsets format as minutes and seconds under an hour, hours above it.</summary>
    [Fact]
    public void FormatOffsetRendersMinutesAndHours()
    {
        Assert.Equal("0:05", AudioTranscriptionProcessor.FormatOffset(5));
        Assert.Equal("2:03", AudioTranscriptionProcessor.FormatOffset(123));
        Assert.Equal("1:01:05", AudioTranscriptionProcessor.FormatOffset(3665));
    }

    /// <summary>Builds a three-segment transcript.</summary>
    /// <returns>The transcript used by most tests.</returns>
    private static SpeechTranscript Transcript() => new()
    {
        Text = "Deploy is green. Ship it. Retro at four.",
        Language = "en",
        Duration = TimeSpan.FromSeconds(12),
        Segments =
        [
            new TranscriptSegment { Index = 0, Start = TimeSpan.FromSeconds(0.5), End = TimeSpan.FromSeconds(3.25), Text = "Deploy is green." },
            new TranscriptSegment { Index = 1, Start = TimeSpan.FromSeconds(3.25), End = TimeSpan.FromSeconds(6), Text = "Ship it." },
            new TranscriptSegment { Index = 2, Start = TimeSpan.FromSeconds(6), End = TimeSpan.FromSeconds(12), Text = "Retro at four." }
        ]
    };

    /// <summary>Builds a throwaway audio payload.</summary>
    /// <returns>A stream standing in for encoded audio.</returns>
    private static MemoryStream AudioStream() => new(Encoding.UTF8.GetBytes("fake-audio-bytes"));
}

/// <summary>
/// A speech-to-text provider that returns a canned transcript and records the options it was given.
/// </summary>
internal sealed class FakeSpeechToText : ISpeechToText
{
    private readonly SpeechTranscript transcript;

    /// <summary>Creates the fake.</summary>
    /// <param name="transcript">The transcript to return.</param>
    public FakeSpeechToText(SpeechTranscript transcript) => this.transcript = transcript;

    /// <inheritdoc/>
    public string Name => "Fake";

    /// <inheritdoc/>
    public string ModelName => "fake-whisper";

    /// <inheritdoc/>
    public IReadOnlyList<string> SupportedExtensions => [".mp3", ".wav"];

    /// <inheritdoc/>
    public bool SupportsSegments => true;

    /// <summary>Gets the options the last call supplied.</summary>
    public SpeechRecognitionOptions? LastOptions { get; private set; }

    /// <inheritdoc/>
    public Task<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string fileName,
        SpeechRecognitionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        return Task.FromResult(transcript);
    }
}
