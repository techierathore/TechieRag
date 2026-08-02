using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for speech-to-text (audio transcription) services (REQ-RAG-041, BRD-122).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a unified interface for turning recorded audio into text so that
/// ingestion, dictation and any other caller can transcribe without binding to one vendor.</para>
/// <para><b>Code Flow:</b> A caller opens an audio stream and hands it to
/// <see cref="TranscribeAsync"/>. <c>AudioTranscriptionProcessor</c> (REQ-RAG-040) is the ingestion
/// consumer: it transcribes, then chunks the transcript for embedding.</para>
/// <para><b>Implementations:</b> OpenAICompatibleSpeechToText. A host may add a platform
/// implementation (for example Apple's Speech framework) without touching this contract.</para>
/// <para><b>Scope:</b> This is FILE transcription — a complete audio payload in, a transcript out.
/// Live streaming dictation is a host concern: it needs microphone capture and OS permissions that
/// no portable library can own, so it is deliberately not modelled here.</para>
/// </remarks>
public interface ISpeechToText
{
    /// <summary>Gets the display name of this speech-to-text provider.</summary>
    string Name { get; }

    /// <summary>Gets the name of the transcription model being used.</summary>
    string ModelName { get; }

    /// <summary>
    /// Gets the audio file extensions this provider accepts (for example ".mp3", ".wav").
    /// </summary>
    /// <remarks>Extensions are lower-case and include the leading dot.</remarks>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Gets whether this provider can return timestamped segments as well as flat text.
    /// </summary>
    /// <remarks>
    /// When false, <see cref="SpeechTranscript.Segments"/> is always empty and consumers must fall
    /// back to plain text chunking.
    /// </remarks>
    bool SupportsSegments { get; }

    /// <summary>
    /// Transcribes an audio stream into text.
    /// </summary>
    /// <param name="audio">The audio content stream, positioned at the start of the payload.</param>
    /// <param name="fileName">
    /// The original file name. Providers use its extension to declare the audio container, so a
    /// meaningful name matters even when the stream is in memory.
    /// </param>
    /// <param name="options">Optional recognition configuration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The transcript, including timestamped segments when the provider supplies them.</returns>
    Task<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string fileName,
        SpeechRecognitionOptions? options = null,
        CancellationToken cancellationToken = default);
}
