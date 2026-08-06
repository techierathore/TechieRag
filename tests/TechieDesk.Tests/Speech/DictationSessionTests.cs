using TechieDesk.Services.Speech;
using Xunit;

namespace TechieDesk.Tests.Speech;

/// <summary>
/// Unit tests for <see cref="DictationSession"/>, the state machine behind the composer's mic
/// button (REQ-UI-035 / BRD-87).
/// </summary>
public class DictationSessionTests
{
    /// <summary>Verifies a fresh session is idle and clickable.</summary>
    [Fact]
    public void NewSessionIsIdleAndToggleable()
    {
        var session = new DictationSession();

        Assert.Equal(DictationStatus.Idle, session.Status);
        Assert.True(session.CanToggle);
        Assert.False(session.IsActive);
        Assert.Null(session.Message);
    }

    /// <summary>Verifies an unsupported build blocks the button permanently.</summary>
    [Fact]
    public void UnsupportedBuildBlocksTheButton()
    {
        var session = new DictationSession();

        session.MarkUnsupported("no recognizer");

        Assert.Equal(DictationStatus.Blocked, session.Status);
        Assert.False(session.CanToggle);
        Assert.Equal("no recognizer", session.Message);
        Assert.False(session.BeginStart("hello"));
    }

    /// <summary>
    /// Verifies a permission refusal leaves the button clickable, because the user can grant access
    /// in System Settings and come straight back.
    /// </summary>
    [Fact]
    public void PermissionDenialLeavesTheButtonClickable()
    {
        var session = new DictationSession();
        session.BeginStart(string.Empty);

        session.MarkPermissionDenied("Microphone access was refused. Grant it in System Settings.");

        Assert.Equal(DictationStatus.Idle, session.Status);
        Assert.True(session.CanToggle);
        Assert.Equal("Microphone access was refused. Grant it in System Settings.", session.Message);
        Assert.Contains("System Settings", session.Message);
    }

    /// <summary>Verifies a second start while listening is ignored.</summary>
    [Fact]
    public void SecondStartWhileListeningIsIgnored()
    {
        var session = new DictationSession();
        session.BeginStart(string.Empty);
        session.MarkListening();

        Assert.False(session.BeginStart("something else"));
        Assert.Equal(DictationStatus.Listening, session.Status);
    }

    /// <summary>Verifies a cumulative transcript replaces the previous one rather than appending.</summary>
    [Fact]
    public void TranscriptUpdatesReplaceRatherThanAccumulate()
    {
        var session = new DictationSession();
        session.BeginStart(string.Empty);

        session.UpdateTranscript("summarise the");
        var text = session.UpdateTranscript("summarise the deck");

        Assert.Equal("summarise the deck", text);
    }

    /// <summary>Verifies dictated words are appended to text the user already typed.</summary>
    [Fact]
    public void DictationAppendsToExistingComposerText()
    {
        var session = new DictationSession();
        session.BeginStart("Summarise");

        var text = session.UpdateTranscript("the deck");

        Assert.Equal("Summarise the deck", text);
    }

    /// <summary>Verifies no double space is inserted when the existing text already ends in one.</summary>
    [Fact]
    public void NoDoubleSpaceWhenExistingTextEndsInWhitespace()
    {
        var session = new DictationSession();
        session.BeginStart("Summarise ");

        var text = session.UpdateTranscript("the deck");

        Assert.Equal("Summarise the deck", text);
    }

    /// <summary>Verifies an empty composer takes the transcript verbatim, with no leading space.</summary>
    [Fact]
    public void EmptyComposerTakesTranscriptVerbatim()
    {
        var session = new DictationSession();
        session.BeginStart(null);

        var text = session.UpdateTranscript("the deck");

        Assert.Equal("the deck", text);
    }

    /// <summary>
    /// Verifies a transcript arriving after the microphone closed is dropped — the recognizer emits
    /// one last revision after a stop, and applying it would repopulate a cleared composer.
    /// </summary>
    [Fact]
    public void TranscriptAfterStopIsDropped()
    {
        var session = new DictationSession();
        session.BeginStart("Summarise");
        session.UpdateTranscript("the deck");
        session.BeginStop();
        session.CompleteStop();

        var text = session.UpdateTranscript("something the user never said");

        Assert.Equal("Summarise the deck", text);
    }

    /// <summary>Verifies a stop keeps the dictated words and returns to idle.</summary>
    [Fact]
    public void StopKeepsTheDictatedText()
    {
        var session = new DictationSession();
        session.BeginStart("Summarise");
        session.UpdateTranscript("the deck");
        Assert.True(session.BeginStop());

        var text = session.CompleteStop();

        Assert.Equal("Summarise the deck", text);
        Assert.Equal(DictationStatus.Idle, session.Status);
        Assert.Equal(string.Empty, session.Transcript);
    }

    /// <summary>Verifies clicks are ignored while a stop is still in flight.</summary>
    [Fact]
    public void ClicksAreIgnoredWhileStopping()
    {
        var session = new DictationSession();
        session.BeginStart(string.Empty);
        session.MarkListening();
        session.BeginStop();

        Assert.False(session.CanToggle);
        Assert.False(session.BeginStart("x"));
        Assert.False(session.BeginStop());
    }

    /// <summary>Verifies a stop with nothing running is a no-op.</summary>
    [Fact]
    public void StopWithNothingRunningIsANoOp()
    {
        var session = new DictationSession();

        Assert.False(session.BeginStop());
        Assert.Equal(DictationStatus.Idle, session.Status);
    }

    /// <summary>Verifies a failure releases the session so the user can try again.</summary>
    [Fact]
    public void FailureReleasesTheSession()
    {
        var session = new DictationSession();
        session.BeginStart("Summarise");
        session.UpdateTranscript("the deck");

        session.Fail("The microphone could not be opened.");

        Assert.Equal(DictationStatus.Idle, session.Status);
        Assert.True(session.CanToggle);
        Assert.Equal("The microphone could not be opened.", session.Message);
        Assert.Equal(string.Empty, session.Transcript);
    }

    /// <summary>Verifies starting a new session clears the previous message.</summary>
    [Fact]
    public void StartingClearsThePreviousMessage()
    {
        var session = new DictationSession();
        session.Fail("boom");

        session.BeginStart(string.Empty);

        Assert.Null(session.Message);
    }
}
