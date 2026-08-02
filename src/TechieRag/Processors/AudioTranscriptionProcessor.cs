using System.Globalization;
using System.Text;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Processors;

/// <summary>
/// Document processor that ingests audio by transcribing it first (REQ-RAG-040, BRD-121).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Makes recordings — meetings, calls, voice notes — searchable through the
/// same ingestion pipeline as every other document type, so a caller adds an <c>.mp3</c> exactly
/// as it adds a <c>.pdf</c>.</para>
/// <para><b>Code Flow:</b>
/// 1) Hands the stream to the configured <see cref="ISpeechToText"/>
/// 2) When the transcript carries timestamped segments, packs consecutive segments into chunks and
///    stamps each chunk with its start/end offset in the audio
/// 3) Otherwise falls back to <see cref="TextChunker"/> over the flat transcript
/// 4) Attaches the language and audio duration to every chunk's metadata
/// </para>
/// <para><b>Dependencies:</b> an <see cref="ISpeechToText"/> supplied by the caller. This processor
/// deliberately owns no transcription engine of its own: which engine is acceptable — a cloud API
/// or a local Whisper server — is a deployment decision, and REQ-NFR-008 makes that decision the
/// operator's rather than the library's.</para>
/// <para><b>Why segment-aware chunking:</b> a transcript chunked purely by character count loses
/// the one thing audio has that text does not — a position to jump back to. Packing whole segments
/// keeps every chunk addressable in the recording, and a chunk never straddles a segment boundary
/// mid-word.</para>
/// </remarks>
public class AudioTranscriptionProcessor : IDocumentProcessor
{
    /// <summary>Metadata key carrying a chunk's start offset in the audio, in seconds.</summary>
    public const string StartSecondsKey = "startSeconds";

    /// <summary>Metadata key carrying a chunk's end offset in the audio, in seconds.</summary>
    public const string EndSecondsKey = "endSeconds";

    /// <summary>Metadata key carrying the transcript language.</summary>
    public const string LanguageKey = "transcriptLanguage";

    /// <summary>Metadata key carrying the full audio duration, in seconds.</summary>
    public const string DurationSecondsKey = "audioDurationSeconds";

    private readonly ISpeechToText speechToText;

    /// <summary>
    /// Creates a processor over the given speech-to-text provider.
    /// </summary>
    /// <param name="speechToText">The transcription provider to ingest audio with.</param>
    /// <exception cref="ArgumentNullException">Thrown when the provider is null.</exception>
    public AudioTranscriptionProcessor(ISpeechToText speechToText)
    {
        ArgumentNullException.ThrowIfNull(speechToText);
        this.speechToText = speechToText;
    }

    /// <summary>
    /// Gets the audio file extensions this processor supports.
    /// </summary>
    /// <value>Whatever the configured <see cref="ISpeechToText"/> accepts — the processor never
    /// claims a format its provider would reject.</value>
    public IReadOnlyList<string> SupportedExtensions => speechToText.SupportedExtensions;

    /// <summary>
    /// Transcribes an audio stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The audio content stream.</param>
    /// <param name="fileName">The original file name (used for metadata and format detection).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the transcript; empty when nothing was spoken.</returns>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <example>
    /// <code>
    /// var stt = new OpenAICompatibleSpeechToText("http://localhost:8080/v1", "", "whisper-1");
    /// var processor = new AudioTranscriptionProcessor(stt);
    /// using var stream = File.OpenRead("standup.mp3");
    /// var chunks = await processor.ProcessAsync(stream, "standup.mp3");
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<TextChunk>> ProcessAsync(
        Stream content,
        string fileName,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);

        options ??= new DocumentProcessingOptions();

        var recognition = new SpeechRecognitionOptions
        {
            Language = options.Language,
            IncludeSegments = true
        };

        var transcript = await speechToText
            .TranscribeAsync(content, fileName, recognition, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(transcript.Text) && transcript.Segments.Count == 0)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();

        return transcript.Segments.Count > 0
            ? ChunkBySegments(transcript, fileName, options, cancellationToken)
            : ChunkByText(transcript, fileName, options, cancellationToken);
    }

    /// <summary>
    /// Packs consecutive transcript segments into chunks bounded by the configured chunk size.
    /// </summary>
    /// <param name="transcript">The transcript being ingested.</param>
    /// <param name="fileName">The original file name.</param>
    /// <param name="options">The processing options in effect.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The chunks, each stamped with its start and end offset in the audio.</returns>
    /// <remarks>
    /// A single segment longer than the chunk size becomes its own chunk rather than being split:
    /// splitting it would produce a chunk whose timestamps no longer bound its text.
    /// </remarks>
    private static List<TextChunk> ChunkBySegments(
        SpeechTranscript transcript,
        string fileName,
        DocumentProcessingOptions options,
        CancellationToken cancellationToken)
    {
        var documentId = Path.GetFileNameWithoutExtension(fileName);
        var chunks = new List<TextChunk>();
        var builder = new StringBuilder();
        var chunkIndex = 0;
        var start = TimeSpan.Zero;
        var end = TimeSpan.Zero;

        foreach (var segment in transcript.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var wouldExceed = builder.Length > 0
                && builder.Length + segment.Text.Length + 1 > options.MaxChunkSize;

            if (wouldExceed)
            {
                chunks.Add(CreateChunk(
                    documentId, builder.ToString(), chunkIndex++, fileName, transcript, options, start, end));
                builder.Clear();
            }

            if (builder.Length == 0)
            {
                start = segment.Start;
            }
            else
            {
                builder.Append(' ');
            }

            builder.Append(segment.Text);
            end = segment.End;
        }

        if (builder.Length > 0)
        {
            chunks.Add(CreateChunk(
                documentId, builder.ToString(), chunkIndex, fileName, transcript, options, start, end));
        }

        return chunks;
    }

    /// <summary>
    /// Chunks a transcript that carries no segment timings, using the shared text chunker.
    /// </summary>
    /// <param name="transcript">The transcript being ingested.</param>
    /// <param name="fileName">The original file name.</param>
    /// <param name="options">The processing options in effect.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The chunks, without audio offsets.</returns>
    private static List<TextChunk> ChunkByText(
        SpeechTranscript transcript,
        string fileName,
        DocumentProcessingOptions options,
        CancellationToken cancellationToken)
    {
        var documentId = Path.GetFileNameWithoutExtension(fileName);
        var chunks = new List<TextChunk>();
        var chunkIndex = 0;

        var textChunks = TextChunker.ChunkText(
            transcript.Text,
            options.MaxChunkSize,
            options.ChunkOverlap,
            options.Chunker);

        foreach (var chunkText in textChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            chunks.Add(CreateChunk(
                documentId, chunkText, chunkIndex++, fileName, transcript, options, null, null));
        }

        return chunks;
    }

    /// <summary>
    /// Builds one chunk with its transcript metadata attached.
    /// </summary>
    /// <param name="documentId">The parent document identifier.</param>
    /// <param name="text">The chunk text.</param>
    /// <param name="chunkIndex">The zero-based chunk position.</param>
    /// <param name="fileName">The source file name.</param>
    /// <param name="transcript">The transcript being ingested.</param>
    /// <param name="options">The processing options in effect.</param>
    /// <param name="start">The chunk's start offset in the audio, when known.</param>
    /// <param name="end">The chunk's end offset in the audio, when known.</param>
    /// <returns>The populated chunk.</returns>
    private static TextChunk CreateChunk(
        string documentId,
        string text,
        int chunkIndex,
        string fileName,
        SpeechTranscript transcript,
        DocumentProcessingOptions options,
        TimeSpan? start,
        TimeSpan? end)
    {
        var metadata = new Dictionary<string, object>
        {
            ["sourceFile"] = fileName,
            ["processorType"] = nameof(AudioTranscriptionProcessor)
        };

        if (!string.IsNullOrWhiteSpace(transcript.Language))
        {
            metadata[LanguageKey] = transcript.Language;
        }

        if (transcript.Duration is not null)
        {
            metadata[DurationSecondsKey] =
                Math.Round(transcript.Duration.Value.TotalSeconds, 3);
        }

        if (start is not null && end is not null)
        {
            metadata[StartSecondsKey] = Math.Round(start.Value.TotalSeconds, 3);
            metadata[EndSecondsKey] = Math.Round(end.Value.TotalSeconds, 3);
        }

        if (options.Metadata is not null)
        {
            foreach (var pair in options.Metadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        return new TextChunk
        {
            DocumentId = documentId,
            Text = text.Trim(),
            ChunkIndex = chunkIndex,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Formats an audio offset as <c>hh:mm:ss</c> for display alongside a retrieval hit.
    /// </summary>
    /// <param name="seconds">The offset in seconds.</param>
    /// <returns>The formatted timestamp.</returns>
    public static string FormatOffset(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
