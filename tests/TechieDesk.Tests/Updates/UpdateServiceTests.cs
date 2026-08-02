using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Data;
using TechieDesk.Services.Updates;
using TechieDesk.Tests.Support;
using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.Updates;

/// <summary>
/// REQ-FN-038b: the decisions the update service makes — is there an update, on which channel, and
/// where does a downloaded package land.
/// </summary>
public sealed class UpdateServiceTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(), "techiedesk-updates-" + Guid.NewGuid().ToString("N"));

    private readonly FakeUpdateFeed feed = new();
    private readonly FakeInstanceSettings settings = new();

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    /// <summary>Nothing newer on the channel means up to date.</summary>
    [Fact]
    public async Task ReportsUpToDateWhenNothingIsNewer()
    {
        feed.Releases = [Release("desktop-v1.0.0")];
        var service = Service("1.0.0");

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.Release);
    }

    /// <summary>An older published release never offers a downgrade.</summary>
    [Fact]
    public async Task NeverOffersADowngrade()
    {
        feed.Releases = [Release("desktop-v1.0.0")];
        var service = Service("2.0.0");

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    /// <summary>A newer stable release is offered with its platform asset.</summary>
    [Fact]
    public async Task OffersANewerRelease()
    {
        feed.Releases = [Release("desktop-v1.1.0", withAssets: true), Release("desktop-v1.0.0")];
        var service = Service("1.0.0");

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("desktop-v1.1.0", result.Release!.TagName);
        Assert.NotNull(result.Asset);
    }

    /// <summary>
    /// A feed failure is reported as a FAILURE. Degrading it to "up to date" would make a network
    /// blip indistinguishable from a successful check and would hide an available security fix
    /// behind the same reassuring message.
    /// </summary>
    [Fact]
    public async Task AFeedFailureIsNeverReportedAsUpToDate()
    {
        feed.Failure = new UpdateFeedException("no network");
        var service = Service("1.0.0");

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.NotEqual(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Equal("no network", result.Error);
    }

    /// <summary>On the stable channel a prerelease is not offered.</summary>
    [Fact]
    public async Task StableChannelIgnoresPrereleases()
    {
        feed.Releases = [Release("desktop-v2.0.0-beta.1", prerelease: true), Release("desktop-v1.0.0")];
        var service = Service("1.0.0");

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    /// <summary>On the prerelease channel the same build is offered.</summary>
    [Fact]
    public async Task PrereleaseChannelOffersPrereleases()
    {
        feed.Releases = [Release("desktop-v2.0.0-beta.1", prerelease: true), Release("desktop-v1.0.0")];
        await settings.SetAsync(UpdatePreferencesStore.PrereleaseKey, "true");
        var service = Service("1.0.0");

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("desktop-v2.0.0-beta.1", result.Release!.TagName);
    }

    /// <summary>
    /// Someone running a beta who leaves the prerelease channel is left where they are, not told to
    /// "update" to an older stable build.
    /// </summary>
    [Fact]
    public async Task LeavingThePrereleaseChannelDoesNotOfferADowngrade()
    {
        feed.Releases = [Release("desktop-v2.0.0-beta.1", prerelease: true), Release("desktop-v1.9.0")];
        var service = Service("2.0.0-beta.1");

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    /// <summary>
    /// An update whose platform build is missing is flagged rather than shown with a dead Download
    /// button — precisely the shape a run whose Windows job failed would produce.
    /// </summary>
    [Fact]
    public async Task FlagsAnUpdateWithNoBuildForThisPlatform()
    {
        feed.Releases = [Release("desktop-v3.0.0", withAssets: false)];
        var service = Service("1.0.0");

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.Asset);
        Assert.True(result.UpdateHasNoDownloadForThisPlatform);
    }

    /// <summary>A completed check records when it ran.</summary>
    [Fact]
    public async Task RecordsWhenTheCheckRan()
    {
        feed.Releases = [Release("desktop-v1.0.0")];
        var service = Service("1.0.0");

        await service.CheckAsync();

        Assert.NotNull(await settings.GetAsync(UpdatePreferencesStore.LastCheckedKey));
    }

    /// <summary>
    /// A failure to record the timestamp does not fail the check. That bookkeeping is a convenience;
    /// letting it break the check would turn a cosmetic problem into "updates are broken".
    /// </summary>
    [Fact]
    public async Task ABookkeepingFailureDoesNotBreakTheCheck()
    {
        feed.Releases = [Release("desktop-v2.0.0", withAssets: true)];
        settings.BreakWrites = true;
        var service = Service("1.0.0");

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
    }

    /// <summary>
    /// THE REQ-FN-037 guard: a downloaded package lands inside the data directory. Written beside
    /// the executable it would land in the read-only application bundle — the identical defect
    /// already found in the Serilog configuration — and would be destroyed by the very update it
    /// installs.
    /// </summary>
    [Fact]
    public async Task DownloadsIntoTheDataDirectoryNotTheApplication()
    {
        var service = Service("1.0.0", payload: "package-bytes");
        var asset = new ReleaseAsset("TechieDesk-2.0.0.dmg", "https://example.test/a.dmg", 13);

        var path = await service.DownloadAsync(asset);

        var expected = Path.Combine(dataDirectory, DataDirectory.DownloadDirectoryName);
        Assert.StartsWith(expected, path, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
        Assert.Equal("package-bytes", await File.ReadAllTextAsync(path));

        // Nothing was written next to the running assembly.
        var appDirectory = Path.GetDirectoryName(typeof(UpdateService).Assembly.Location)!;
        Assert.False(File.Exists(Path.Combine(appDirectory, asset.Name)));
    }

    /// <summary>
    /// The asset name is remote input. A traversal in it must not escape the download directory —
    /// otherwise whoever controls the feed can choose where the file is written.
    /// </summary>
    [Fact]
    public async Task AnAssetNameCannotEscapeTheDownloadDirectory()
    {
        var service = Service("1.0.0", payload: "x");
        var asset = new ReleaseAsset(
            "../../escaped.dmg", "https://example.test/a.dmg", 1);

        var path = await service.DownloadAsync(asset);

        var expected = Path.Combine(dataDirectory, DataDirectory.DownloadDirectoryName);
        Assert.StartsWith(expected, Path.GetFullPath(path), StringComparison.Ordinal);
        Assert.Equal("escaped.dmg", Path.GetFileName(path));
    }

    /// <summary>
    /// A truncated download is rejected and leaves nothing behind. Kept under the real file name it
    /// would sit there looking installable and fail confusingly at install time.
    /// </summary>
    [Fact]
    public async Task RejectsATruncatedDownload()
    {
        var service = Service("1.0.0", payload: "short");
        var asset = new ReleaseAsset("TechieDesk-2.0.0.dmg", "https://example.test/a.dmg", 999999);

        await Assert.ThrowsAsync<UpdateFeedException>(() => service.DownloadAsync(asset));

        var directory = Path.Combine(dataDirectory, DataDirectory.DownloadDirectoryName);
        Assert.Empty(Directory.GetFiles(directory));
    }

    /// <summary>A failed download reports rather than leaving a half-written package.</summary>
    [Fact]
    public async Task ReportsAFailedDownload()
    {
        var service = Service("1.0.0", payload: string.Empty, downloadStatus: HttpStatusCode.NotFound);
        var asset = new ReleaseAsset("TechieDesk-2.0.0.dmg", "https://example.test/a.dmg", 10);

        await Assert.ThrowsAsync<UpdateFeedException>(() => service.DownloadAsync(asset));
    }

    private UpdateService Service(
        string currentVersion,
        string payload = "",
        HttpStatusCode downloadStatus = HttpStatusCode.OK)
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(downloadStatus)
        {
            Content = new StringContent(payload),
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataDirectory.ConfigKey] = dataDirectory,
            })
            .Build();

        return new UpdateService(
            feed,
            new FixedVersionProvider(currentVersion),
            new UpdatePreferencesStore(settings, Options.Create(new UpdateOptions())),
            new HttpClient(handler),
            configuration,
            TimeProvider.System,
            NullLogger<UpdateService>.Instance);
    }

    private static AvailableRelease Release(string tag, bool prerelease = false, bool withAssets = false)
    {
        ReleaseVersion.TryParse(tag, out var version);
        var assets = withAssets
            ? new List<ReleaseAsset>
            {
                new("TechieDesk.dmg", "https://example.test/a.dmg", 10),
                new("TechieDesk.zip", "https://example.test/a.zip", 10),
            }
            : [];

        return new AvailableRelease(
            version, tag, tag, prerelease || version.IsPrerelease, "notes",
            "https://example.test", DateTimeOffset.UnixEpoch, assets);
    }

    private sealed class FakeUpdateFeed : IUpdateFeed
    {
        public IReadOnlyList<AvailableRelease> Releases { get; set; } = [];

        public UpdateFeedException? Failure { get; set; }

        public Task<IReadOnlyList<AvailableRelease>> GetReleasesAsync(
            CancellationToken cancellationToken = default) =>
            Failure is not null ? throw Failure : Task.FromResult(Releases);
    }

    private sealed class FixedVersionProvider : IAppVersionProvider
    {
        public FixedVersionProvider(string version)
        {
            ReleaseVersion.TryParse(version, out var parsed);
            Current = parsed;
            RawVersion = version;
        }

        public ReleaseVersion Current { get; }

        public string RawVersion { get; }
    }

    private sealed class FakeInstanceSettings : IInstanceSettingRepository
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public bool BreakWrites { get; set; }

        public Task<string?> GetAsync(string settingKey) =>
            Task.FromResult(values.TryGetValue(settingKey, out var value) ? value : null);

        public string? Get(string settingKey) =>
            values.TryGetValue(settingKey, out var value) ? value : null;

        public Task SetAsync(string settingKey, string settingValue)
        {
            if (BreakWrites)
            {
                throw new InvalidOperationException("settings store unavailable");
            }

            values[settingKey] = settingValue;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InstanceSetting>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<InstanceSetting>>([]);
    }
}
