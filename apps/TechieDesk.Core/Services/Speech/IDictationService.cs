namespace TechieDesk.Services.Speech;

/// <summary>
/// Platform microphone dictation, as the head implements it (REQ-UI-035, BRD-87).
/// </summary>
/// <remarks>
/// <para><b>Why this is not the library's <c>ISpeechToText</c>.</b> That contract transcribes a
/// COMPLETE audio payload — a file in, a transcript out. Dictation is the opposite shape: it opens
/// the microphone, streams partial results while the user is still talking, and needs an OS
/// permission grant before it may start. None of that is portable, so it stays a head concern and
/// the library abstraction stays free of a microphone.</para>
/// <para><b>Why it lives in Core.</b> Same rule as <c>ISecretStore</c> (REQ-FN-039): Core states
/// the contract so the net10.0 test project can exercise the logic around it; the MAUI head owns
/// the platform implementation.</para>
/// <para><b>Implementations:</b> <c>CatalystDictationService</c> in the head (Apple's Speech
/// framework), <c>UnsupportedDictationService</c> everywhere else.</para>
/// </remarks>
public interface IDictationService
{
    /// <summary>Gets whether this build can dictate at all.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Gets the reason dictation is unavailable, for display; null when <see cref="IsSupported"/>
    /// is true.
    /// </summary>
    string? UnsupportedReason { get; }

    /// <summary>
    /// Asks the OS for microphone and speech-recognition access.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The resulting permission state.</returns>
    /// <remarks>
    /// The first call raises a system prompt the user must answer. A denial is remembered by the OS,
    /// so re-calling does not re-prompt — the caller must send the user to System Settings instead.
    /// </remarks>
    Task<DictationPermission> RequestPermissionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the microphone and begins recognising speech.
    /// </summary>
    /// <param name="callbacks">Callbacks for transcript updates and failures.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes once capture has started.</returns>
    Task StartAsync(DictationCallbacks callbacks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops capture and releases the microphone.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes once the microphone is released.</returns>
    /// <remarks>Safe to call when nothing is running.</remarks>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of a microphone/speech permission request (REQ-UI-035).
/// </summary>
public enum DictationPermission
{
    /// <summary>The OS granted access.</summary>
    Granted,

    /// <summary>The user refused, or a policy blocks access. The OS will not prompt again.</summary>
    Denied,

    /// <summary>This build cannot dictate, so no permission was requested.</summary>
    Unsupported
}

/// <summary>
/// Callbacks a dictation session raises while the microphone is open (REQ-UI-035).
/// </summary>
/// <remarks>
/// <para><b>Why callbacks rather than events:</b> a Razor component needs to await
/// <c>InvokeAsync(StateHasChanged)</c> from whichever thread the recognizer signals on, and an
/// event handler cannot be awaited.</para>
/// </remarks>
public sealed class DictationCallbacks
{
    /// <summary>
    /// Gets the handler invoked with the transcript so far, each time recognition refines it.
    /// </summary>
    /// <remarks>
    /// The text is CUMULATIVE for the session, not a delta: recognizers revise earlier words as
    /// later context arrives, so an append-only consumer would keep the discarded guesses.
    /// </remarks>
    public Func<string, Task>? OnTranscriptUpdated { get; init; }

    /// <summary>Gets the handler invoked when the session ends in an error.</summary>
    public Func<string, Task>? OnFailed { get; init; }

    /// <summary>Gets the spoken language as a BCP-47 tag; null uses the system locale.</summary>
    public string? Language { get; init; }
}
