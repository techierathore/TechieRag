using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for text-to-speech (speech synthesis) services (REQ-RAG-041, BRD-122).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a unified interface for rendering text as spoken audio, so a
/// read-aloud feature can move between a platform synthesiser and a provider voice without the
/// caller changing.</para>
/// <para><b>Code Flow:</b> A caller passes text plus an optional voice/rate; the provider returns
/// the encoded audio bytes. Playback is the caller's concern — this contract deliberately does not
/// reach for an audio device, because a library that owns the speaker cannot be used from a
/// background service or a test.</para>
/// <para><b>Implementations:</b> OpenAICompatibleTextToSpeech. Hosts that speak through the OS
/// (AVSpeechSynthesizer on macOS, SAPI on Windows) implement the host-side read-aloud service
/// instead: they never produce a byte payload, so they are not modelled by this contract.</para>
/// </remarks>
public interface ITextToSpeech
{
    /// <summary>Gets the display name of this text-to-speech provider.</summary>
    string Name { get; }

    /// <summary>Gets the name of the synthesis model being used.</summary>
    string ModelName { get; }

    /// <summary>
    /// Gets the audio formats this provider can emit (for example "mp3", "wav", "opus").
    /// </summary>
    /// <remarks>Format names are lower-case and carry no leading dot.</remarks>
    IReadOnlyList<string> SupportedFormats { get; }

    /// <summary>
    /// Renders text as spoken audio.
    /// </summary>
    /// <param name="text">The text to speak. Must not be empty.</param>
    /// <param name="options">Optional voice, language, rate and format configuration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The encoded audio together with its media type.</returns>
    Task<SpeechAudio> SynthesizeAsync(
        string text,
        SpeechSynthesisOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the voices this provider offers.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The available voices; empty when the provider exposes none.</returns>
    Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default);
}
