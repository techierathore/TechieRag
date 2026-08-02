using TechieRag.Web;
using Xunit;

namespace TechieRag.Tests.Web.Live;

/// <summary>
/// REQ-RAG-018 / BRD-62: <see cref="YouTubeTranscriptReader"/> against real YouTube.
/// </summary>
/// <remarks>
/// <para><b>This file is a canary, not a formality.</b> The reader parses an undocumented internal
/// shape out of the watch page and then fetches a signed timed-text URL. Both halves belong to
/// YouTube and can change without notice, and no hermetic test can ever detect it — a fake watch
/// page will keep returning the shape it was written with for as long as the file exists. The only
/// instrument that can tell whether transcript ingestion works today is today's YouTube.</para>
/// <para><b>The two halves are tested separately on purpose.</b> When this goes red, the first
/// question is which half broke: did the watch page stop exposing <c>captionTracks</c>, or did the
/// timed-text endpoint stop serving? Those have completely different fixes, so
/// <see cref="RealWatchPageStillExposesCaptionTracks"/> deliberately duplicates the reader's
/// discovery step over a plain client and asserts only that, independent of the download.</para>
/// </remarks>
[Trait("Category", LiveNetworkFactAttribute.CategoryName)]
public sealed class LiveYouTubeTranscriptTests : IDisposable
{
    private readonly HttpClient httpClient = CreateWatchPageClient();

    /// <summary>
    /// Half one: a real watch page still carries the <c>captionTracks</c> array the reader locates.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT use the reader. If this passes while
    /// <see cref="TranscriptIsReadFromARealVideoWithCaptions"/> fails, the page shape is intact and
    /// the failure is downstream, in the timed-text download.
    /// </remarks>
    [LiveNetworkFact]
    public async Task RealWatchPageStillExposesCaptionTracks()
    {
        var page = await httpClient.GetStringAsync(LiveTargets.VideoWithCaptions);

        Assert.Contains("\"captionTracks\"", page, StringComparison.Ordinal);
        Assert.Contains("\"baseUrl\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Half two, and the acceptance for REQ-RAG-018: a real video that has captions yields a real,
    /// non-empty transcript.
    /// </summary>
    /// <remarks>
    /// <b>Known red as of 2026-07-27 (TR-RAG-015).</b> The watch page still lists caption tracks, but
    /// YouTube answers every timed-text URL derived from it with HTTP 200 and a zero-byte body, for
    /// every video and every <c>fmt</c>, so no transcript can be produced without an authorised
    /// client. The assertion is left as the requirement states it rather than relaxed to match the
    /// breakage: this test is the signal that transcript ingestion is unavailable, and weakening it
    /// would delete that signal.
    /// </remarks>
    [LiveNetworkFact]
    public async Task TranscriptIsReadFromARealVideoWithCaptions()
    {
        var transcript = await Reader().ReadAsync(LiveTargets.VideoWithCaptions);

        Assert.False(string.IsNullOrWhiteSpace(transcript.Text), "The transcript came back empty.");
        Assert.True(
            transcript.Text.Length > 200,
            $"Expected a real transcript, got {transcript.Text.Length} characters.");
        Assert.Equal("aircAruvnKk", transcript.VideoId);
        Assert.False(string.IsNullOrWhiteSpace(transcript.Title));

        // Timed text is doubly HTML-encoded. Undecoded output reads as markup inside the embedding.
        Assert.DoesNotContain("&amp;#39;", transcript.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("&quot;", transcript.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A real video with no captions fails loudly with a reason an operator can act on, rather than
    /// ingesting an empty document.
    /// </summary>
    /// <remarks>
    /// This is the failure mode that matters more than the success one. A transcript that came back
    /// blank and was ingested anyway produces a document that can never be retrieved, sitting in the
    /// library looking like a video in which nobody said anything.
    /// </remarks>
    [LiveNetworkFact]
    public async Task RealVideoWithoutCaptionsFailsWithAnOperatorFacingReason()
    {
        var error = await Assert.ThrowsAsync<WebFetchException>(
            () => Reader().ReadAsync(LiveTargets.VideoWithoutCaptions));

        Assert.Contains("caption", error.Message, StringComparison.OrdinalIgnoreCase);

        // Operator-facing means it reads as a sentence about the video, not as a parser diagnostic.
        Assert.DoesNotContain("Exception", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("null", error.Message, StringComparison.Ordinal);
        Assert.EndsWith(".", error.Message.TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A URL that is not a YouTube video is rejected before any request is made.
    /// </summary>
    [LiveNetworkFact]
    public async Task NonYouTubeUrlIsRejectedOutright()
    {
        var error = await Assert.ThrowsAsync<WebFetchException>(
            () => Reader().ReadAsync(LiveTargets.TinyPageWithOneOffHostLink));

        Assert.Contains("not a YouTube video URL", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Releases the shared client.</summary>
    public void Dispose() => httpClient.Dispose();

    private YouTubeTranscriptReader Reader() => new(httpClient);

    /// <summary>
    /// A client shaped like the one the application registers for the transcript reader.
    /// </summary>
    /// <remarks>
    /// The watch page is served differently to clients that do not look like browsers, so the
    /// <c>Accept-Language</c> and browser-shaped <c>Accept</c> here mirror
    /// <c>AddTechieDeskWebIngestion</c> exactly. A test that fetched with different headers would be
    /// exercising a different response than the product does.
    /// </remarks>
    private static HttpClient CreateWatchPageClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
        })
        {
            Timeout = TimeSpan.FromSeconds(45),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "TechieDesk/1.0 (+https://github.com/techierathore/TechieRag)");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }
}
