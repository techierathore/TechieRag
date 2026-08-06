namespace TechieRag.Models;

/// <summary>
/// Configuration options for a text-to-speech synthesis request (REQ-RAG-041).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a caller pick a voice, language, speaking rate and container format
/// per request without naming a provider-specific type.</para>
/// </remarks>
public class SpeechSynthesisOptions
{
    /// <summary>
    /// Gets or sets the identifier of the voice to speak with.
    /// </summary>
    /// <remarks>Null uses the provider's default voice. Values come from
    /// <see cref="TechieRag.Abstractions.ITextToSpeech.GetVoicesAsync"/>.</remarks>
    public string? VoiceId { get; set; }

    /// <summary>Gets or sets the spoken language as an ISO-639-1 or BCP-47 code.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the speaking rate, where 1.0 is the provider's normal pace.
    /// </summary>
    /// <remarks>Null leaves the provider default in place.</remarks>
    public double? SpeakingRate { get; set; }

    /// <summary>
    /// Gets or sets the requested audio format (for example "mp3", "wav", "opus").
    /// </summary>
    /// <remarks>Null asks the provider for its default format.</remarks>
    public string? Format { get; set; }
}

/// <summary>
/// Encoded audio produced by a text-to-speech provider (REQ-RAG-041).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Returns the synthesised bytes together with enough type information for a
/// caller to play, save or stream them without guessing the container.</para>
/// </remarks>
public class SpeechAudio
{
    /// <summary>Gets the encoded audio bytes.</summary>
    public required byte[] Data { get; init; }

    /// <summary>Gets the audio format name (for example "mp3").</summary>
    public required string Format { get; init; }

    /// <summary>Gets the IANA media type (for example "audio/mpeg").</summary>
    public required string ContentType { get; init; }
}

/// <summary>
/// A voice offered by a text-to-speech provider (REQ-RAG-041).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Describes a selectable voice so a UI can list it without hard-coding a
/// provider's catalogue.</para>
/// </remarks>
public class SpeechVoice
{
    /// <summary>Gets the identifier passed back as <see cref="SpeechSynthesisOptions.VoiceId"/>.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the human-readable voice name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the voice's language as an ISO-639-1 or BCP-47 code, when known.</summary>
    public string? Language { get; init; }
}
