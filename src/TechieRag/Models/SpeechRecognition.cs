namespace TechieRag.Models;

/// <summary>
/// Configuration options for a speech-to-text transcription request (REQ-RAG-041).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a caller steer recognition per request without a provider-specific
/// type leaking into the call site.</para>
/// </remarks>
public class SpeechRecognitionOptions
{
    /// <summary>
    /// Gets or sets the spoken language as an ISO-639-1 code (for example "en", "de").
    /// </summary>
    /// <remarks>Null lets the provider auto-detect, which is slower and less accurate.</remarks>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets a prompt that biases recognition towards expected vocabulary.
    /// </summary>
    /// <remarks>
    /// Useful for domain jargon, product names and spellings the model would otherwise mis-hear.
    /// </remarks>
    public string? Prompt { get; set; }

    /// <summary>
    /// Gets or sets the sampling temperature, from 0.0 (deterministic) to 1.0.
    /// </summary>
    /// <remarks>Null leaves the provider default in place.</remarks>
    public double? Temperature { get; set; }

    /// <summary>
    /// Gets or sets whether timestamped segments are requested alongside the flat transcript.
    /// </summary>
    /// <remarks>
    /// Default is true: segments are what let ingestion attach a play position to every chunk.
    /// Providers that cannot supply them return an empty segment list regardless.
    /// </remarks>
    public bool IncludeSegments { get; set; } = true;
}

/// <summary>
/// The result of a speech-to-text transcription (REQ-RAG-041).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries both the flat transcript and, where the provider supplies them,
/// the timestamped segments that let a consumer map text back to a position in the audio.</para>
/// </remarks>
public class SpeechTranscript
{
    /// <summary>Gets the full transcript text.</summary>
    public required string Text { get; init; }

    /// <summary>Gets the detected or requested language as an ISO-639-1 code, when known.</summary>
    public string? Language { get; init; }

    /// <summary>Gets the duration of the transcribed audio, when the provider reports it.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Gets the timestamped segments, in order; empty when none were produced.</summary>
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = [];
}

/// <summary>
/// One timestamped span of a transcript (REQ-RAG-041).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Ties a run of transcript text to its start and end offsets in the source
/// audio, so a retrieval hit can point at the moment it came from.</para>
/// </remarks>
public class TranscriptSegment
{
    /// <summary>Gets the zero-based position of this segment within the transcript.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the offset from the start of the audio at which this segment begins.</summary>
    public required TimeSpan Start { get; init; }

    /// <summary>Gets the offset from the start of the audio at which this segment ends.</summary>
    public required TimeSpan End { get; init; }

    /// <summary>Gets the text spoken during this segment.</summary>
    public required string Text { get; init; }
}
