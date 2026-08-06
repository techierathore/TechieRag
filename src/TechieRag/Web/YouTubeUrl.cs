using System.Text.RegularExpressions;

namespace TechieRag.Web;

/// <summary>
/// Recognises YouTube URLs and extracts the video id (REQ-RAG-018 / BRD-62).
/// </summary>
public static partial class YouTubeUrl
{
    /// <summary>Hosts that carry YouTube video ids.</summary>
    private static readonly string[] KnownHosts =
    [
        "youtube.com", "www.youtube.com", "m.youtube.com",
        "music.youtube.com", "youtu.be", "www.youtu.be",
    ];

    /// <summary>Determines whether a URL points at a YouTube video.</summary>
    /// <param name="url">Candidate URL.</param>
    /// <returns>True when a video id can be extracted.</returns>
    public static bool IsYouTube(string? url) => TryGetVideoId(url, out _);

    /// <summary>Extracts the 11-character video id from any of YouTube's URL shapes.</summary>
    /// <param name="url">Candidate URL.</param>
    /// <param name="videoId">The extracted id when this returns true.</param>
    /// <returns>True when the URL is a recognisable YouTube video URL.</returns>
    /// <remarks>
    /// Handles <c>watch?v=</c>, the <c>youtu.be/</c> short form, and the <c>/embed/</c>, <c>/shorts/</c>
    /// and <c>/live/</c> paths. Ids are validated against the 11-character alphabet rather than taken
    /// on trust, so a malformed URL fails here instead of producing a confident request for a video
    /// that cannot exist.
    /// </remarks>
    public static bool TryGetVideoId(string? url, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            // Bare ids are accepted so a user can paste one out of a URL they already trimmed.
            return IsWellFormedId(url.Trim()) && Assign(url.Trim(), out videoId);
        }

        if (!KnownHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var shortId = uri.AbsolutePath.Trim('/');
            return IsWellFormedId(shortId) && Assign(shortId, out videoId);
        }

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var fromQuery = query["v"];
        if (IsWellFormedId(fromQuery))
        {
            return Assign(fromQuery!, out videoId);
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2
            && segments[0] is "embed" or "shorts" or "live" or "v"
            && IsWellFormedId(segments[1]))
        {
            return Assign(segments[1], out videoId);
        }

        return false;
    }

    private static bool Assign(string value, out string target)
    {
        target = value;
        return true;
    }

    private static bool IsWellFormedId(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) && VideoIdPattern().IsMatch(candidate);

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdPattern();
}
