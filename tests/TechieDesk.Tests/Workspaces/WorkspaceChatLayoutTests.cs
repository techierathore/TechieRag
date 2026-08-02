using System.Text.RegularExpressions;
using Xunit;

namespace TechieDesk.Tests.Workspaces;

/// <summary>
/// The workspace chat column's geometry invariants (REQ-UI-044 / BRD-137), asserted against the
/// shipped stylesheet and component rather than against a rendered DOM.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why arithmetic and not a rendered bounding box.</b> This is a net10.0 test project that
/// references TechieDesk.Core only — the MAUI head that owns <c>WorkspaceChat.razor</c> targets
/// <c>net10.0-maccatalyst</c> and cannot be referenced from here (see the csproj note on
/// REQ-FN-035), and the project carries no bUnit, no AngleSharp and no Playwright. There is
/// therefore no way to measure a real <c>getBoundingClientRect</c> in-process. What CAN be asserted
/// is the arithmetic the layout is built on, read out of the files that ship: the transcript's
/// height budget is a single number in <c>base.css</c>, and everything else in the chat column is
/// fixed-height chrome whose measured size is recorded below. If the budget is smaller than the
/// chrome, controls leave the viewport — which is exactly the 2026-07-31 defect.
/// </para>
/// <para>
/// <b>Where the constants come from.</b> They are read off the verify sweep's own capture of the
/// running app at 1024x720, <c>test-results/ui-verify/workspace-chat-n1024.json</c> — not guessed.
/// In that capture the transcript box was <c>max-height:56vh</c>; the window's web viewport is
/// 690 CSS px tall (720 window px less the 30 px macOS title bar), so the transcript rendered
/// 386 px tall. The composer's foot row (<c>Button:Attach</c>) was reported at screen y 785, i.e.
/// viewport y 783 once the window origin (40) and the title bar (30) are removed, and below it sit
/// the mode-hint line (16), the card's bottom padding (16) and the page's bottom padding (24).
/// The chat column therefore ended 839 px down a 690 px viewport, and the part of it that is NOT
/// the transcript measured 839 - 386 = 453 px.
/// </para>
/// </remarks>
public class WorkspaceChatLayoutTests
{
    /// <summary>
    /// The web viewport height, in CSS px, of the REQ-UI-041 floor window (1024x720 less the
    /// 30 px macOS title bar). Measured from the sweep's own window rect.
    /// </summary>
    private const int FloorViewportHeight = 690;

    /// <summary>The web viewport height of the wide sweep window (1600x1240 less the title bar).</summary>
    private const int WideViewportHeight = 1210;

    /// <summary>
    /// Everything in the chat column that is NOT the transcript, in CSS px, measured at 1024 wide
    /// where the composer's control bar wraps onto three rows and is at its tallest: the page and
    /// card padding, the workspace action row, the composer bar, the 84 px textarea row, the
    /// Attach/Prompts foot and the mode hint. See the remarks for the derivation.
    /// </summary>
    private const int MeasuredChromeAtFloor = 453;

    /// <summary>
    /// The smallest transcript that is still a transcript. Below this the message list stops being
    /// able to show a question and its answer together, which is a worse screen than a scrollbar.
    /// </summary>
    private const int UsableTranscriptFloor = 128;

    /// <summary>
    /// The transcript's height budget must reserve at least as much room as the chrome that has to
    /// stay on screen with it, or the composer's own controls are pushed below the window at the
    /// REQ-UI-041 720 px floor — the 2026-07-31 defect, where Attach, Prompts, the keyboard hint
    /// and the mode hint were all reported off-window.
    /// </summary>
    [Fact]
    public void TranscriptBudgetKeepsTheComposerInsideTheFloorViewport()
    {
        var reserve = TranscriptReserve();

        Assert.True(
            reserve >= MeasuredChromeAtFloor,
            $".td-chat-transcript reserves {reserve}px for the rest of the chat column, but the " +
            $"column's fixed chrome measures {MeasuredChromeAtFloor}px at 1024 wide. The composer " +
            $"would be pushed {MeasuredChromeAtFloor - reserve}px below the {FloorViewportHeight}px " +
            "floor viewport.");

        Assert.True(
            FloorViewportHeight - reserve >= UsableTranscriptFloor,
            $"The transcript collapses to {FloorViewportHeight - reserve}px at the REQ-UI-041 floor, " +
            $"below the {UsableTranscriptFloor}px that still shows a turn.");
    }

    /// <summary>
    /// The budget is expressed against the viewport, so the wide window must not lose transcript
    /// height relative to the 56vh box the fix replaces (685 px as captured at 1600x1240).
    /// </summary>
    [Fact]
    public void TranscriptBudgetDoesNotShrinkTheWideWindow()
    {
        var transcript = WideViewportHeight - TranscriptReserve();

        Assert.True(
            transcript >= 685,
            $"The transcript is {transcript}px at 1600x1240; the 56vh box it replaces was 685px.");
    }

    /// <summary>
    /// The transcript's height must come from the stylesheet's documented budget, not from an
    /// inline viewport percentage. A percentage cannot express "the viewport minus the chrome":
    /// 56vh fitted a 1240 px window and overflowed a 720 px one, which is what made this a
    /// height-driven defect that only showed up at the floor.
    /// </summary>
    [Fact]
    public void TranscriptIsSizedByTheStylesheetNotAnInlineViewportPercentage()
    {
        var markup = ReadComponent();
        var transcript = TranscriptElement(markup);

        Assert.Contains("td-chat-transcript", transcript);
        Assert.DoesNotContain("vh", transcript);
        Assert.DoesNotContain("style=", transcript);
    }

    /// <summary>
    /// The transcript must be scrolled to the newest message. Anchoring it at the newest turn is
    /// what a chat is expected to do, and it is also what keeps the scrolled-out history from being
    /// laid out over the composer: a list parked at the top lays its overflow out BELOW the box,
    /// which is where the composer's Send button sits, and that is the pair the 2026-07-31 sweep
    /// measured intersecting by 980 px at 1024x720.
    /// </summary>
    [Fact]
    public void TranscriptScrollsToTheNewestMessage()
    {
        var markup = ReadComponent();

        Assert.Contains("id=\"@TranscriptElementId\"", TranscriptElement(markup));
        Assert.Contains("scrollToEnd", markup);
        Assert.Contains("export function scrollToEnd", ReadComposerModule());
    }

    /// <summary>
    /// An assistant message's action row must sit at the START of the bubble, with the bubble's
    /// other affordances. Right-aligning it inside a bubble that is itself sized by its content put
    /// the read-aloud control in a column that moves with the message text — at 1024 it landed in
    /// the composer's Send/Dictate band, at 1600 it did not, which is why one width overlapped and
    /// the other did not.
    /// </summary>
    [Fact]
    public void AssistantActionRowIsAlignedWithTheBubbleNotItsRightEdge()
    {
        var markup = ReadComponent();
        var actionRow = Section(markup, "<ReadAloudButton");

        Assert.DoesNotContain("justify-end", actionRow);
    }

    /// <summary>Reads the <c>max-height: calc(100vh - Npx)</c> reserve from <c>.td-chat-transcript</c>.</summary>
    /// <returns>The reserved pixels.</returns>
    /// <exception cref="InvalidOperationException">The rule is missing or is not a calc budget.</exception>
    private static int TranscriptReserve()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "apps", "TechieDesk", "wwwroot", "styles", "base.css"));
        var match = Regex.Match(
            css,
            @"\.td-chat-transcript\s*\{[^}]*?max-height:\s*calc\(\s*100vh\s*-\s*(\d+)px\s*\)",
            RegexOptions.Singleline);

        if (!match.Success)
        {
            throw new InvalidOperationException(
                "base.css has no '.td-chat-transcript { … max-height: calc(100vh - Npx) }' rule, so the " +
                "workspace chat transcript has no height budget to check.");
        }

        return int.Parse(match.Groups[1].Value);
    }

    /// <summary>Reads WorkspaceChat.razor from the working tree.</summary>
    /// <returns>The component's source.</returns>
    private static string ReadComponent() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(), "apps", "TechieDesk", "Components", "Pages", "WorkspaceChat.razor"));

    /// <summary>Reads the composer JS module from the working tree.</summary>
    /// <returns>The module's source.</returns>
    private static string ReadComposerModule() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(), "apps", "TechieDesk", "wwwroot", "js", "composer.js"));

    /// <summary>Extracts the opening tag of the transcript scroll container.</summary>
    /// <param name="markup">The component's source.</param>
    /// <returns>The element's opening tag.</returns>
    /// <exception cref="InvalidOperationException">No scrolling transcript container was found.</exception>
    private static string TranscriptElement(string markup)
    {
        var match = Regex.Match(markup, @"<div[^>]*overflow-y-auto[^>]*>");
        if (!match.Success)
        {
            throw new InvalidOperationException(
                "WorkspaceChat.razor no longer has a scrolling transcript container.");
        }

        return match.Value;
    }

    /// <summary>Returns the markup line carrying <paramref name="anchor"/> and the line above it.</summary>
    /// <param name="markup">The component's source.</param>
    /// <param name="anchor">The text to find.</param>
    /// <returns>The two lines, joined.</returns>
    /// <exception cref="InvalidOperationException">The anchor is not in the file.</exception>
    private static string Section(string markup, string anchor)
    {
        var lines = markup.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(anchor, StringComparison.Ordinal))
            {
                return (i > 0 ? lines[i - 1] : string.Empty) + "\n" + lines[i];
            }
        }

        throw new InvalidOperationException($"'{anchor}' is not in WorkspaceChat.razor.");
    }

    /// <summary>
    /// Walks up from the test output directory to the folder holding <c>TechieRag.slnx</c>.
    /// </summary>
    /// <returns>The repository root.</returns>
    /// <exception cref="InvalidOperationException">The root could not be found.</exception>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechieRag.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException(
            "Could not find TechieRag.slnx above " + AppContext.BaseDirectory + ".");
    }
}
