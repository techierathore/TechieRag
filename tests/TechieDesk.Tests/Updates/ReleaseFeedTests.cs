using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Updates;
using TechieDesk.Tests.Support;
using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.Updates;

/// <summary>
/// REQ-FN-038b: what the app accepts from the release feed, and what it does when the feed will not
/// answer. The feed is remote input, so every one of these is a trust boundary.
/// </summary>
public sealed class ReleaseFeedTests
{
    private const string TwoDesktopReleases = """
        [
          {
            "tag_name": "desktop-v1.3.0",
            "name": "TechieDesk 1.3.0",
            "body": "Newer.",
            "html_url": "https://example.test/releases/1.3.0",
            "draft": false,
            "prerelease": false,
            "published_at": "2026-07-20T10:00:00Z",
            "assets": [
              { "name": "TechieDesk-1.3.0-macos-universal-unsigned.dmg",
                "browser_download_url": "https://example.test/1.3.0.dmg", "size": 76000000 },
              { "name": "TechieDesk-1.3.0-windows-x64-unsigned.zip",
                "browser_download_url": "https://example.test/1.3.0.zip", "size": 91000000 }
            ]
          },
          {
            "tag_name": "desktop-v1.2.0",
            "name": "TechieDesk 1.2.0",
            "body": "Older.",
            "html_url": "https://example.test/releases/1.2.0",
            "draft": false,
            "prerelease": false,
            "assets": []
          }
        ]
        """;

    private static GitHubReleaseFeed Feed(
        HttpStatusCode status,
        string json,
        out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(status, json));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        return new GitHubReleaseFeed(
            client,
            Options.Create(new UpdateOptions()),
            NullLogger<GitHubReleaseFeed>.Instance);
    }

    /// <summary>
    /// Pins the wire field names, which are <b>snake_case</b>. This test exists because the code
    /// originally relied on <c>JsonSerializerDefaults.Web</c>'s camelCase policy, which matches none
    /// of them: every property bound to null, every release failed its tag check, the list came back
    /// empty and the app would have reported "up to date" forever — with no exception and no log
    /// line. The first fixtures were written in camelCase too, so they passed against the bug. The
    /// field names below are copied from a live API response.
    /// </summary>
    [Fact]
    public async Task BindsTheSnakeCaseFieldNamesTheApiActuallySends()
    {
        const string live = """
            [{
              "html_url": "https://example.test/releases/tag/desktop-v4.5.6",
              "tag_name": "desktop-v4.5.6",
              "name": "TechieDesk 4.5.6",
              "draft": false,
              "prerelease": false,
              "published_at": "2026-07-01T08:00:00Z",
              "body": "Release notes here.",
              "assets": [{
                "name": "TechieDesk-4.5.6-macos-universal-unsigned.dmg",
                "browser_download_url": "https://example.test/download/TechieDesk.dmg",
                "size": 76543210
              }]
            }]
            """;
        var feed = Feed(HttpStatusCode.OK, live, out _);

        var releases = await feed.GetReleasesAsync();

        var release = Assert.Single(releases);
        Assert.Equal("desktop-v4.5.6", release.TagName);
        Assert.Equal("https://example.test/releases/tag/desktop-v4.5.6", release.ReleasePageUrl);
        Assert.Equal("Release notes here.", release.ReleaseNotes);
        Assert.Equal(2026, release.PublishedAtUtc!.Value.Year);
        var asset = Assert.Single(release.Assets);
        Assert.Equal("https://example.test/download/TechieDesk.dmg", asset.DownloadUrl);
        Assert.Equal(76543210, asset.SizeBytes);
    }

    /// <summary>Desktop releases map across with their assets, newest first.</summary>
    [Fact]
    public async Task ReadsDesktopReleasesNewestFirst()
    {
        var feed = Feed(HttpStatusCode.OK, TwoDesktopReleases, out _);

        var releases = await feed.GetReleasesAsync();

        Assert.Equal(2, releases.Count);
        Assert.Equal("desktop-v1.3.0", releases[0].TagName);
        Assert.Equal("desktop-v1.2.0", releases[1].TagName);
        Assert.Equal(2, releases[0].Assets.Count);
        Assert.Equal(76000000, releases[0].Assets[0].SizeBytes);
    }

    /// <summary>
    /// The library's own <c>v*</c> tags are ignored. This repository publishes the TechieRag NuGet
    /// package under those, and without the prefix filter a library release would be offered as an
    /// application update — with an unrelated version number, so usually as a large "upgrade".
    /// </summary>
    [Fact]
    public async Task IgnoresLibraryTags()
    {
        const string mixed = """
            [
              { "tag_name": "v9.9.9", "name": "TechieRag library", "draft": false, "prerelease": false },
              { "tag_name": "desktop-v1.0.0", "name": "TechieDesk", "draft": false, "prerelease": false }
            ]
            """;
        var feed = Feed(HttpStatusCode.OK, mixed, out _);

        var releases = await feed.GetReleasesAsync();

        Assert.Single(releases);
        Assert.Equal("desktop-v1.0.0", releases[0].TagName);
    }

    /// <summary>Drafts are not published releases and are never offered.</summary>
    [Fact]
    public async Task IgnoresDrafts()
    {
        const string withDraft = """
            [
              { "tag_name": "desktop-v2.0.0", "draft": true, "prerelease": false },
              { "tag_name": "desktop-v1.0.0", "draft": false, "prerelease": false }
            ]
            """;
        var feed = Feed(HttpStatusCode.OK, withDraft, out _);

        var releases = await feed.GetReleasesAsync();

        Assert.Single(releases);
        Assert.Equal("desktop-v1.0.0", releases[0].TagName);
    }

    /// <summary>
    /// A <c>-beta</c> tag is a prerelease even when the publisher forgot to tick the box, because
    /// the tag is what the version comparison already believes.
    /// </summary>
    [Fact]
    public async Task TreatsABetaTagAsPrereleaseEvenIfUnflagged()
    {
        const string unflagged = """
            [{ "tag_name": "desktop-v1.5.0-beta.1", "draft": false, "prerelease": false }]
            """;
        var feed = Feed(HttpStatusCode.OK, unflagged, out _);

        var releases = await feed.GetReleasesAsync();

        Assert.True(releases[0].IsPrerelease);
    }

    /// <summary>The API version and a User-Agent are sent; no credential ever is.</summary>
    [Fact]
    public async Task SendsNoCredential()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TechieDesk/1.0");
        var feed = new GitHubReleaseFeed(
            client, Options.Create(new UpdateOptions()), NullLogger<GitHubReleaseFeed>.Instance);

        await feed.GetReleasesAsync();

        var call = Assert.Single(handler.Calls);
        Assert.DoesNotContain("Authorization", call.Headers.Keys);
    }

    /// <summary>An error status is reported, never treated as "no releases".</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReportsAnErrorStatus(HttpStatusCode status)
    {
        var feed = Feed(status, "{}", out _);

        await Assert.ThrowsAsync<UpdateFeedException>(() => feed.GetReleasesAsync());
    }

    /// <summary>A payload that cannot be parsed is reported, not swallowed into an empty list.</summary>
    [Fact]
    public async Task ReportsAnUnreadablePayload()
    {
        var feed = Feed(HttpStatusCode.OK, "this is not json", out _);

        await Assert.ThrowsAsync<UpdateFeedException>(() => feed.GetReleasesAsync());
    }

    /// <summary>A transport failure surfaces as a feed failure rather than escaping raw.</summary>
    [Fact]
    public async Task ReportsATransportFailure()
    {
        var handler = new ThrowingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        var feed = new GitHubReleaseFeed(
            client, Options.Create(new UpdateOptions()), NullLogger<GitHubReleaseFeed>.Instance);

        await Assert.ThrowsAsync<UpdateFeedException>(() => feed.GetReleasesAsync());
    }

    /// <summary>The right artefact is chosen per platform.</summary>
    [Fact]
    public async Task SelectsThePlatformArtefact()
    {
        var feed = Feed(HttpStatusCode.OK, TwoDesktopReleases, out _);
        var releases = await feed.GetReleasesAsync();

        Assert.EndsWith(".dmg", releases[0].AssetFor(DataDirectoryPlatform.MacOS)!.Name);
        Assert.EndsWith(".zip", releases[0].AssetFor(DataDirectoryPlatform.Windows)!.Name);
    }

    /// <summary>
    /// Asset selection survives the artefact being renamed when signing lands. Today every file
    /// carries an <c>-unsigned</c> segment that REQ-FN-038c removes; matching the whole name would
    /// mean every existing install stopped finding downloads on the first signed release.
    /// </summary>
    [Fact]
    public async Task StillFindsTheAssetOnceItIsSigned()
    {
        const string signed = """
            [{
              "tag_name": "desktop-v2.0.0", "draft": false, "prerelease": false,
              "assets": [
                { "name": "TechieDesk-2.0.0.dmg", "browser_download_url": "https://example.test/a.dmg", "size": 1 },
                { "name": "TechieDesk-2.0.0-win.zip", "browser_download_url": "https://example.test/a.zip", "size": 1 }
              ]
            }]
            """;
        var feed = Feed(HttpStatusCode.OK, signed, out _);

        var releases = await feed.GetReleasesAsync();

        Assert.NotNull(releases[0].AssetFor(DataDirectoryPlatform.MacOS));
        Assert.NotNull(releases[0].AssetFor(DataDirectoryPlatform.Windows));
    }

    /// <summary>A release with no build for this platform yields no asset rather than a wrong one.</summary>
    [Fact]
    public async Task ReturnsNoAssetWhenThePlatformHasNoBuild()
    {
        const string macOnly = """
            [{
              "tag_name": "desktop-v2.0.0", "draft": false, "prerelease": false,
              "assets": [
                { "name": "TechieDesk-2.0.0.dmg", "browser_download_url": "https://example.test/a.dmg", "size": 1 }
              ]
            }]
            """;
        var feed = Feed(HttpStatusCode.OK, macOnly, out _);

        var releases = await feed.GetReleasesAsync();

        Assert.NotNull(releases[0].AssetFor(DataDirectoryPlatform.MacOS));
        Assert.Null(releases[0].AssetFor(DataDirectoryPlatform.Windows));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no network");
    }
}
