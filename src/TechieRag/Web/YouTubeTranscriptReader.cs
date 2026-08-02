using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Web;

/// <summary>
/// Reads the caption track of a YouTube video as plain text (REQ-RAG-018 / BRD-62).
/// </summary>
/// <remarks>
/// <para><b>How, and why it is fragile.</b> YouTube publishes no public transcript API. The only
/// route without a Data API key is to load the watch page, read the <c>captionTracks</c> array out
/// of the embedded player JSON, and fetch the timed-text URL it names. That is an undocumented
/// internal shape, so <b>this will break when YouTube changes it</b> — not "may". It is written to
/// fail loudly with an operator-facing reason rather than return empty text, because a transcript
/// that silently comes back blank would ingest an empty document and look like a video with nothing
/// said in it.</para>
/// <para><b>Auto-generated captions count.</b> Most videos have no human transcript, so refusing
/// ASR tracks would make this useless on the majority of real inputs. A manual track is preferred
/// when one exists, since ASR punctuation is poor and chunking follows punctuation.</para>
/// </remarks>
public sealed partial class YouTubeTranscriptReader
{
    private readonly HttpClient httpClient;
    private readonly ILogger<YouTubeTranscriptReader> logger;

    /// <summary>Initializes a new instance of the <see cref="YouTubeTranscriptReader"/> class.</summary>
    /// <param name="httpClient">Client used to fetch the watch page and the caption track.</param>
    /// <param name="logger">Diagnostics.</param>
    public YouTubeTranscriptReader(HttpClient httpClient, ILogger<YouTubeTranscriptReader>? logger = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.logger = logger ?? NullLogger<YouTubeTranscriptReader>.Instance;
    }

    /// <summary>Reads a video's transcript.</summary>
    /// <param name="urlOrVideoId">A YouTube URL in any recognised shape, or a bare video id.</param>
    /// <param name="preferredLanguage">BCP-47 prefix to prefer, e.g. "en". Null takes the first track.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The video title and its transcript text.</returns>
    /// <exception cref="WebFetchException">The video has no captions, or the page could not be read.</exception>
    public async Task<YouTubeTranscript> ReadAsync(
        string urlOrVideoId,
        string? preferredLanguage = "en",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(urlOrVideoId);

        if (!YouTubeUrl.TryGetVideoId(urlOrVideoId, out var videoId))
        {
            throw new WebFetchException(urlOrVideoId, $"'{urlOrVideoId}' is not a YouTube video URL.");
        }

        var watchUrl = $"https://www.youtube.com/watch?v={videoId}";
        string page;
        try
        {
            page = await httpClient.GetStringAsync(watchUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new WebFetchException(watchUrl, $"The video page could not be loaded: {ex.Message}", ex);
        }

        var title = ExtractTitle(page) ?? videoId;
        var track = SelectTrack(page, preferredLanguage, watchUrl);

        string trackXml;
        try
        {
            trackXml = await httpClient.GetStringAsync(track, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new WebFetchException(watchUrl, $"The caption track could not be downloaded: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(trackXml))
        {
            // Observed against live YouTube on 2026-07-27 for every video and every fmt: the watch
            // page still lists the tracks, and the signed timed-text URL it names answers HTTP 200
            // with a zero-byte body. Reporting this as "the video has no captions" would blame the
            // video for a restriction on the caller, and the operator would go looking for a
            // different video instead of understanding that no video will work.
            throw new WebFetchException(
                watchUrl,
                "YouTube listed a caption track for this video but served it empty, which it now does "
                + "for callers it has not authorised. The transcript could not be read, and nothing "
                + "was ingested.");
        }

        var text = ParseTimedText(trackXml);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new WebFetchException(
                watchUrl,
                "The caption track for this video contained no readable text, so nothing was ingested.");
        }

        logger.LogInformation("Read transcript for {VideoId} ({Characters} characters)", videoId, text.Length);
        return new YouTubeTranscript(videoId, title, watchUrl, text);
    }

    private static string SelectTrack(string page, string? preferredLanguage, string watchUrl)
    {
        var json = ExtractCaptionTracksJson(page);
        if (json is null)
        {
            // Two different situations reach here, and telling them apart is the difference between
            // an operator picking another video and an operator raising a bug. A watch page with no
            // player response at all is a consent wall or a bot check, not a video without captions.
            throw new WebFetchException(
                watchUrl,
                page.Contains("ytInitialPlayerResponse", StringComparison.Ordinal)
                    ? "This video has no captions, so there is no transcript to ingest."
                    : "YouTube served a page with no player data, so the caption list could not be "
                      + "read. This is usually a consent or bot check rather than a video without "
                      + "captions.");
        }

        List<CaptionTrack> tracks;
        try
        {
            tracks = JsonSerializer.Deserialize<List<CaptionTrack>>(json) ?? [];
        }
        catch (JsonException ex)
        {
            throw new WebFetchException(
                watchUrl,
                "YouTube returned a caption list TechieRag could not read. The page format has probably changed.",
                ex);
        }

        var usable = tracks.Where(t => !string.IsNullOrWhiteSpace(t.BaseUrl)).ToList();
        if (usable.Count == 0)
        {
            throw new WebFetchException(watchUrl, "This video has no usable caption track.");
        }

        // Preference order: requested language and human-authored, then requested language at all,
        // then anything. ASR punctuation is poor and chunk boundaries follow punctuation, so a manual
        // track produces materially better retrieval.
        var chosen = usable.FirstOrDefault(t => Matches(t, preferredLanguage) && t.Kind != "asr")
                     ?? usable.FirstOrDefault(t => Matches(t, preferredLanguage))
                     ?? usable[0];

        return WebUtility.HtmlDecode(chosen.BaseUrl!);
    }

    /// <summary>
    /// Cuts the <c>captionTracks</c> array out of the player JSON by matching brackets.
    /// </summary>
    /// <remarks>
    /// <para>A regular expression cannot do this correctly and the obvious one is actively wrong.
    /// <c>"captionTracks":(\[.*?\])</c> stops at the first <c>]</c> it sees, and a track's
    /// <c>name</c> is sometimes <c>{"simpleText":…}</c> and sometimes <c>{"runs":[…]}</c> — the
    /// second shape puts a <c>]</c> inside the first element, so the lazy match returns a truncated
    /// fragment that then fails to parse. The result is a video with 31 caption tracks reported as
    /// having none.</para>
    /// <para>Counting brackets while skipping over string literals is the only way to take exactly
    /// the array. String awareness matters as much as the counting: a <c>baseUrl</c> is a URL and
    /// may legitimately contain a bracket.</para>
    /// </remarks>
    /// <param name="page">The watch page HTML.</param>
    /// <returns>The JSON array text, or null when the page carries no caption track list.</returns>
    private static string? ExtractCaptionTracksJson(string page)
    {
        const string key = "\"captionTracks\":";

        var start = page.IndexOf(key, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var open = page.IndexOf('[', start + key.Length);
        if (open < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = open; index < page.Length; index++)
        {
            var character = page[index];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inString)
            {
                if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    break;
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        return page[open..(index + 1)];
                    }

                    break;
            }
        }

        // An unterminated array means a truncated page, not an absent caption list.
        return null;
    }

    private static bool Matches(CaptionTrack track, string? language) =>
        language is null
        || (track.LanguageCode?.StartsWith(language, StringComparison.OrdinalIgnoreCase) ?? false);

    private static string? ExtractTitle(string page)
    {
        var match = TitlePattern().Match(page);
        if (!match.Success)
        {
            return null;
        }

        var title = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
        const string suffix = " - YouTube";
        if (title.EndsWith(suffix, StringComparison.Ordinal))
        {
            title = title[..^suffix.Length];
        }

        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string ParseTimedText(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return string.Empty;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var element in document.Descendants("text"))
        {
            // Timed text is doubly encoded: the XML holds HTML entities, so decoding once leaves
            // "&amp;#39;" in the output and the embedded text reads as markup.
            var line = WebUtility.HtmlDecode(WebUtility.HtmlDecode(element.Value)).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            builder.Append(line);
            // Caption lines are wrapped for display, not sentences. Joining with a space keeps
            // sentences intact for the chunker; a newline per cue would fragment every sentence.
            builder.Append(line.EndsWith('.') || line.EndsWith('?') || line.EndsWith('!') ? '\n' : ' ');
        }

        return builder.ToString().Trim();
    }

    private sealed class CaptionTrack
    {
        [System.Text.Json.Serialization.JsonPropertyName("baseUrl")]
        public string? BaseUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("languageCode")]
        public string? LanguageCode { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("kind")]
        public string? Kind { get; set; }
    }

    [GeneratedRegex("<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TitlePattern();
}

/// <summary>A video's transcript (REQ-RAG-018).</summary>
/// <param name="VideoId">The 11-character video id.</param>
/// <param name="Title">The video title, or the id when the page carried none.</param>
/// <param name="Url">Canonical watch URL, recorded as the document's source.</param>
/// <param name="Text">The transcript.</param>
public sealed record YouTubeTranscript(string VideoId, string Title, string Url, string Text);
