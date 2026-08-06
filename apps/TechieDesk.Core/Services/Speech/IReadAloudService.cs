namespace TechieDesk.Services.Speech;

/// <summary>
/// Platform speech synthesis for reading an assistant response aloud (REQ-UI-036, BRD-88).
/// </summary>
/// <remarks>
/// <para><b>Why this is not the library's <c>ITextToSpeech</c>.</b> That contract returns encoded
/// audio BYTES, which a caller then plays. The platform synthesiser never produces bytes — it
/// speaks straight to the output device — so modelling it as a byte-returning provider would mean
/// inventing a payload nobody wants. Provider voices, when they arrive, plug in behind
/// <c>ITextToSpeech</c> and are played by an implementation of THIS contract.</para>
/// <para><b>Implementations:</b> <c>MauiReadAloudService</c> in the head (AVSpeechSynthesizer on
/// Mac Catalyst, SAPI on Windows, through MAUI Essentials), <c>UnsupportedReadAloudService</c>
/// everywhere else.</para>
/// </remarks>
public interface IReadAloudService
{
    /// <summary>Gets whether this build can speak at all.</summary>
    bool IsSupported { get; }

    /// <summary>Gets whether speech is currently playing.</summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Gets whether this machine holds a voice that can actually pronounce a language.
    /// </summary>
    /// <param name="culture">A culture name such as <c>hi</c> or <c>hi-IN</c>.</param>
    /// <param name="cancellationToken">Token to abandon the query.</param>
    /// <returns>True only when a voice for that language is installed.</returns>
    /// <remarks>
    /// <para>
    /// <b>REQ-UI-055 / BRD-91.</b> The question this exists to answer is not "does TechieDesk have a
    /// Hindi translation" — that is <c>SupportedLanguages</c> — but "can this Mac SAY Hindi", which
    /// is a different fact and a per-machine one. macOS ships Devanagari voices (Lekha, hi-IN) as an
    /// optional download, so a machine running a Hindi TechieDesk may well have no voice for it; the
    /// synthesiser then skips the characters instead of speaking them, and the listener gets silence
    /// where a sentence should have been.
    /// </para>
    /// <para>
    /// Asynchronous because the platform answer is: on Mac Catalyst the voice list comes back from
    /// <c>AVSpeechSynthesisVoice.GetSpeechVoices</c> through MAUI Essentials' <c>GetLocalesAsync</c>,
    /// and on Windows from the SAPI enumeration. Callers should treat a false as "speak the
    /// invariant English", never as "say nothing".
    /// </para>
    /// </remarks>
    Task<bool> CanSpeakAsync(string culture, CancellationToken cancellationToken = default);

    /// <summary>
    /// Speaks the given text, replacing anything already playing.
    /// </summary>
    /// <param name="text">The text to speak. Empty text is ignored.</param>
    /// <param name="culture">
    /// The language <paramref name="text"/> is written in, so the synthesiser picks a voice that can
    /// pronounce it. Null leaves the platform default in place, which is the language of the OS
    /// rather than of the app — pass the app's culture whenever it is known.
    /// </param>
    /// <param name="cancellationToken">Token to stop playback part-way.</param>
    /// <returns>A task that completes when playback finishes or is stopped.</returns>
    Task SpeakAsync(string text, string? culture = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops playback immediately.
    /// </summary>
    /// <returns>A task that completes once playback has stopped.</returns>
    /// <remarks>Safe to call when nothing is playing.</remarks>
    Task StopAsync();
}
