using System.Text.RegularExpressions;
using TechieRag.Models;
using Xunit;
using Xunit.Abstractions;

namespace TechieRag.Local.Tests.Live;

/// <summary>
/// The local picture model for real, on this machine's processor: the library downloads, checks and loads
/// Phi-3.5 vision, reads a fixed calendar page into text, and runs the DevGuide's sample
/// (REQ-RAG-145, REQ-RAG-149, REQ-RAG-150 / BRD-203, BRD-207, BRD-208).
/// </summary>
[Collection(LiveLocalLlmCollection.Name)]
public sealed partial class LiveVisionTests
{
    /// <summary>
    /// The context the live tests load with: the calendar page takes 1,960 tokens with its prompt (measured
    /// 2026-11-05), which leaves room for the answer, and the load stays within a laptop's free memory.
    /// </summary>
    private const int LiveContextSize = 3_072;

    /// <summary>The meetings on <c>Fixtures/calendar-page.png</c>: each title and its time as the page shows it.</summary>
    private static readonly (string Title, int Hour, int Minute)[] CalendarMeetings =
    [
        ("Weekly planning", 9, 0),
        ("Budget review with Priya", 10, 30),
        ("Lunch with suppliers", 12, 15),
        ("Client call with Northwind", 14, 0),
        ("Team retrospective", 16, 45)
    ];

    private readonly ITestOutputHelper output;

    /// <summary>Creates the tests.</summary>
    /// <param name="output">Where the model's text is written, so a run shows what it read.</param>
    public LiveVisionTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    /// <summary>
    /// On a laptop with no graphics card, the library downloads Phi-3.5 vision from Hugging Face, every file
    /// passes its fingerprint check, and the model loads.
    /// </summary>
    [LiveVisionDownloadFact(DisplayName = "REQ-RAG-145 LiveVisionModelDownloadsAndLoads")]
    public async Task LiveVisionModelDownloadsAndLoads()
    {
        var model = LocalModel.Phi35VisionInstruct;
        using var provider = new LocalLlmProvider(new LocalLlmOptions { Model = model, TermsAccepted = true, ContextSize = LiveContextSize });

        await provider.LoadAsync();

        var variant = Assert.Single(model.Variants);
        Assert.True(provider.IsLoaded);
        Assert.True(variant.IsOnDisk(model.GetDirectory(variant)));
        Assert.True(provider.SupportsInput(ChatContentKind.Image));
    }

    /// <summary>
    /// The fixed picture of a calendar page goes to the local picture model, and its text contains every
    /// meeting title on the page and each one's time.
    /// </summary>
    [LiveVisionFact(DisplayName = "REQ-RAG-150 LiveCalendarPageTitlesAndTimes")]
    public async Task LiveCalendarPageTitlesAndTimes()
    {
        using var provider = new LocalLlmProvider(new LocalLlmOptions
        {
            Model = LocalModel.Phi35VisionInstruct,
            TermsAccepted = true,
            ContextSize = LiveContextSize
        });
        var turn = ChatMessage.UserWithImages(
            "This is a page of an office calendar. Write one line for each meeting on it, in the form HH:MM - title, "
            + "copying the time from the Time column and the title from the Meeting column exactly.",
            ChatImage.FromBytes(await File.ReadAllBytesAsync(CalendarPicturePath()), "image/png"));

        var reply = await provider.ChatAsync([turn], new LlmCompletionOptions { MaxTokens = 200, Temperature = 0 });

        var text = reply.Content ?? string.Empty;
        output.WriteLine(text);
        output.WriteLine($"Input tokens: {reply.Usage.InputTokens}; output tokens: {reply.Usage.OutputTokens}.");
        var normalised = Normalise(text);
        Assert.All(CalendarMeetings, meeting =>
        {
            Assert.True(normalised.Contains(Normalise(meeting.Title), StringComparison.Ordinal), $"The text has no '{meeting.Title}':\n{text}");
            Assert.True(TimeForms(meeting.Hour, meeting.Minute).Any(form => normalised.Contains(form, StringComparison.Ordinal)),
                $"The text has no time {meeting.Hour:00}:{meeting.Minute:00} for '{meeting.Title}':\n{text}");
        });
    }

    /// <summary>
    /// The DevGuide's sample, copied here line for line: a picture in, text out, on the local provider, with
    /// the same <see cref="ChatImage"/> and <see cref="ChatContentPart"/> code a hosted provider takes.
    /// </summary>
    [LiveVisionFact(DisplayName = "REQ-RAG-149 LiveDevGuideSamplePictureInTextOut")]
    public async Task LiveDevGuideSamplePictureInTextOut()
    {
        // --- DevGuide sample: begin ---
        using var llm = new LocalLlmProvider(new LocalLlmOptions
        {
            ReadsImages = true,   // no model named: Phi-3.5 vision on a desktop
            TermsAccepted = true, // the user accepted the model's MIT licence earlier
            ContextSize = 3072    // a page-sized picture takes about 2,000 tokens
        });

        var picture = await File.ReadAllBytesAsync(CalendarPicturePath());
        var turn = ChatMessage.UserWithImages(
            "Describe this picture in two sentences.",
            ChatImage.FromBytes(picture, "image/png"));

        var reply = await llm.ChatAsync([turn], new LlmCompletionOptions { MaxTokens = 120 });
        Console.WriteLine(reply.Content);
        // --- DevGuide sample: end ---

        output.WriteLine(reply.Content);
        Assert.Same(LocalModel.Phi35VisionInstruct, llm.Model);
        Assert.False(string.IsNullOrWhiteSpace(reply.Content));
    }

    /// <summary>Gets the fixed calendar page beside the test assembly.</summary>
    /// <returns>The file's path.</returns>
    internal static string CalendarPicturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "calendar-page.png");

    /// <summary>The ways a model may write a time: 24-hour with or without a leading zero, and 12-hour.</summary>
    /// <param name="hour">The hour, 0 to 23.</param>
    /// <param name="minute">The minute.</param>
    /// <returns>The written forms, normalised.</returns>
    private static IEnumerable<string> TimeForms(int hour, int minute)
    {
        yield return $"{hour:00}:{minute:00}";
        yield return $"{hour}:{minute:00}";
        var twelve = hour % 12 == 0 ? 12 : hour % 12;
        yield return $"{twelve}:{minute:00} {(hour < 12 ? "am" : "pm")}";
        yield return $"{twelve}:{minute:00}{(hour < 12 ? "am" : "pm")}";
        yield return $"{twelve}:{minute:00} {(hour < 12 ? "a m" : "p m")}";
    }

    /// <summary>Lower-cases, turns punctuation other than <c>:</c> into spaces and collapses spaces, so copying is judged, not typography.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The normalised text.</returns>
    private static string Normalise(string text) =>
        Spaces().Replace(Punctuation().Replace(text.ToLowerInvariant(), " "), " ").Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}:]+")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
