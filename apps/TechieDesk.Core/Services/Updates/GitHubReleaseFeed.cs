using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TechieDesk.Services.Updates;

/// <summary>
/// Reads published desktop releases from the GitHub Releases API (REQ-FN-038b).
/// </summary>
/// <remarks>
/// <para><b>Anonymous by design.</b> This sends no token. The releases of a public repository are
/// public, and an update check is the one call the app makes before anyone has signed in — attaching
/// a credential would leak who is running the app to the feed host on every launch, for no gain. The
/// cost is GitHub's unauthenticated rate limit, which a per-launch check cannot come close to.</para>
/// <para><b>Only <c>desktop-v</c> tags.</b> This repository also publishes the TechieRag NuGet
/// library under <c>v*</c> tags. Without the prefix filter a library release would be offered as an
/// application update, and the version numbers are unrelated, so it would usually look like a large
/// upgrade.</para>
/// </remarks>
public sealed class GitHubReleaseFeed : IUpdateFeed
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly UpdateOptions options;
    private readonly ILogger<GitHubReleaseFeed> logger;

    /// <summary>Initializes a new instance of the <see cref="GitHubReleaseFeed"/> class.</summary>
    /// <param name="httpClient">Configured client for the feed host.</param>
    /// <param name="options">Feed location.</param>
    /// <param name="logger">Diagnostics.</param>
    public GitHubReleaseFeed(
        HttpClient httpClient,
        IOptions<UpdateOptions> options,
        ILogger<GitHubReleaseFeed> logger)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableRelease>> GetReleasesAsync(
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient
                .GetAsync(options.ReleasesPath(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Update check could not reach the release feed");
            throw new UpdateFeedException(
                "TechieDesk could not reach the update service. Check your network connection and try again.",
                ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // A 404 is worth naming separately: it means the feed is pointed somewhere that does
                // not exist (a renamed or private repository), which no amount of retrying fixes.
                var detail = response.StatusCode == HttpStatusCode.NotFound
                    ? $"No releases were found for {options.RepositoryOwner}/{options.RepositoryName}."
                    : $"The update service replied {(int)response.StatusCode} ({response.ReasonPhrase}).";

                logger.LogWarning("Update feed returned {StatusCode}", response.StatusCode);
                throw new UpdateFeedException(detail);
            }

            try
            {
                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                var payload = await JsonSerializer
                    .DeserializeAsync<List<GitHubRelease>>(stream, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);

                return Map(payload);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Update feed returned a payload that could not be read");
                throw new UpdateFeedException("The update service returned a response TechieDesk could not read.", ex);
            }
        }
    }

    private static IReadOnlyList<AvailableRelease> Map(List<GitHubRelease>? payload)
    {
        if (payload is null)
        {
            return [];
        }

        var releases = new List<AvailableRelease>(payload.Count);
        foreach (var item in payload)
        {
            if (item.Draft || string.IsNullOrWhiteSpace(item.TagName))
            {
                continue;
            }

            if (!item.TagName.StartsWith(ReleaseVersion.TagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ReleaseVersion.TryParse(item.TagName, out var version))
            {
                continue;
            }

            var assets = item.Assets?
                .Where(asset => !string.IsNullOrWhiteSpace(asset.Name)
                                && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                .Select(asset => new ReleaseAsset(asset.Name!, asset.BrowserDownloadUrl!, asset.Size))
                .ToList() ?? [];

            releases.Add(new AvailableRelease(
                version,
                item.TagName,
                string.IsNullOrWhiteSpace(item.Name) ? item.TagName : item.Name!,
                // Trust the version string over the publisher's checkbox when they disagree: a tag of
                // -beta is a prerelease whether or not anyone ticked the box.
                item.Prerelease || version.IsPrerelease,
                item.Body ?? string.Empty,
                item.HtmlUrl ?? string.Empty,
                item.PublishedAt,
                assets));
        }

        releases.Sort((left, right) => right.Version.CompareTo(left.Version));
        return releases;
    }

    // Property names are stated EXPLICITLY rather than left to a naming policy. The API returns
    // snake_case (tag_name, browser_download_url, published_at, html_url); JsonSerializerDefaults.Web
    // applies a camelCase policy, which matches none of them. Relying on it bound every property to
    // null, so each release failed its tag check, the list came back empty, and the app reported
    // "up to date" forever — a silent, permanent failure of the entire feature that no exception
    // would ever have revealed.
    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
