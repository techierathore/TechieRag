namespace TechieDesk.Services.Speech;

/// <summary>
/// The state machine behind the composer's mic button (REQ-UI-035, BRD-87).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Holds every decision the dictation button makes — what the button shows,
/// when a click is ignored, how a live transcript merges into text the user already typed — apart
/// from the microphone itself, so all of it is testable on a machine with no audio hardware and no
/// permission grant.</para>
/// <para><b>Why a class and not component fields:</b> the component may not own this logic. Mic
/// capture ends asynchronously, from a callback thread, possibly after the user has navigated away;
/// keeping the transitions in one place is what stops "stop" arriving while "start" is still in
/// flight and leaving the button stuck showing a live mic.</para>
/// </remarks>
public sealed class DictationSession
{
    /// <summary>The hint shown when macOS has refused microphone or speech access.</summary>
    public const string DeniedHint =
        "Microphone or speech access was refused. Grant it in System Settings › Privacy & Security "
        + "› Microphone (and Speech Recognition), then try again.";

    private string committedText = string.Empty;

    /// <summary>Gets the current state of the session.</summary>
    public DictationStatus Status { get; private set; } = DictationStatus.Idle;

    /// <summary>Gets the transcript recognised so far in this session.</summary>
    public string Transcript { get; private set; } = string.Empty;

    /// <summary>Gets the message to show beside the button, or null when there is nothing to say.</summary>
    public string? Message { get; private set; }

    /// <summary>Gets whether the microphone is open or in the process of opening.</summary>
    public bool IsActive => Status is DictationStatus.Starting or DictationStatus.Listening;

    /// <summary>Gets whether a click on the mic button should do anything.</summary>
    public bool CanToggle => Status != DictationStatus.Blocked && Status != DictationStatus.Stopping;

    /// <summary>
    /// Records that this build cannot dictate at all, which is a permanent state.
    /// </summary>
    /// <param name="reason">The reason to show the user.</param>
    public void MarkUnsupported(string reason)
    {
        Status = DictationStatus.Blocked;
        Message = reason;
    }

    /// <summary>
    /// Records that the OS refused microphone or speech access.
    /// </summary>
    /// <remarks>
    /// This is NOT permanent — the user can grant access in System Settings and come back — so the
    /// session returns to Idle rather than Blocked, and the button stays clickable.
    /// </remarks>
    public void MarkPermissionDenied()
    {
        Status = DictationStatus.Idle;
        Message = DeniedHint;
    }

    /// <summary>
    /// Begins a start, taking the transcript the composer already holds as the base to append to.
    /// </summary>
    /// <param name="existingText">Text already in the composer.</param>
    /// <returns>True when the caller should open the microphone; false when the click is a no-op.</returns>
    public bool BeginStart(string? existingText)
    {
        if (!CanToggle || IsActive)
        {
            return false;
        }

        committedText = existingText ?? string.Empty;
        Transcript = string.Empty;
        Message = null;
        Status = DictationStatus.Starting;
        return true;
    }

    /// <summary>Records that the microphone is open and recognition has begun.</summary>
    public void MarkListening()
    {
        if (Status == DictationStatus.Starting)
        {
            Status = DictationStatus.Listening;
        }
    }

    /// <summary>
    /// Accepts a cumulative transcript update from the recognizer.
    /// </summary>
    /// <param name="transcript">The transcript recognised so far.</param>
    /// <returns>The full text the composer should now show.</returns>
    /// <remarks>
    /// Updates arriving after a stop are dropped: the recognizer can emit one final revision after
    /// the microphone is closed, and applying it would re-populate a composer the user has cleared.
    /// </remarks>
    public string UpdateTranscript(string? transcript)
    {
        if (!IsActive)
        {
            return committedText;
        }

        MarkListening();
        Transcript = transcript ?? string.Empty;
        return ComposedText();
    }

    /// <summary>
    /// Begins a stop.
    /// </summary>
    /// <returns>True when the caller should close the microphone; false when nothing is running.</returns>
    public bool BeginStop()
    {
        if (!IsActive)
        {
            return false;
        }

        Status = DictationStatus.Stopping;
        return true;
    }

    /// <summary>
    /// Completes a stop and returns the text the composer should keep.
    /// </summary>
    /// <returns>The composer text, with the dictated words merged in.</returns>
    public string CompleteStop()
    {
        var text = ComposedText();
        committedText = text;
        Transcript = string.Empty;
        Status = DictationStatus.Idle;
        return text;
    }

    /// <summary>
    /// Records a failure, releasing the session so the user can try again.
    /// </summary>
    /// <param name="message">The failure message to show.</param>
    public void Fail(string message)
    {
        Status = DictationStatus.Idle;
        Transcript = string.Empty;
        Message = message;
    }

    /// <summary>Clears any message currently shown.</summary>
    public void ClearMessage() => Message = null;

    /// <summary>
    /// Merges the live transcript onto the text the composer already held.
    /// </summary>
    /// <returns>The combined text.</returns>
    /// <remarks>
    /// A separator is inserted only when both halves are non-empty AND the existing text does not
    /// already end in whitespace — dictating after "Summarise " must not produce a double space, and
    /// dictating after "Summarise" must not produce "Summarisethe deck".
    /// </remarks>
    private string ComposedText()
    {
        if (Transcript.Length == 0)
        {
            return committedText;
        }

        if (committedText.Length == 0)
        {
            return Transcript;
        }

        var separator = char.IsWhiteSpace(committedText[^1]) ? string.Empty : " ";
        return committedText + separator + Transcript;
    }
}

/// <summary>
/// The states a dictation session moves through (REQ-UI-035).
/// </summary>
public enum DictationStatus
{
    /// <summary>Not dictating; a click starts a session.</summary>
    Idle,

    /// <summary>Permission and microphone start-up are in flight.</summary>
    Starting,

    /// <summary>The microphone is open and transcripts are arriving.</summary>
    Listening,

    /// <summary>A stop is in flight; further clicks are ignored until it completes.</summary>
    Stopping,

    /// <summary>Dictation is unavailable in this build; the button is disabled.</summary>
    Blocked
}
