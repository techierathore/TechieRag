using Microsoft.Extensions.Options;
using TechieDeskDb;

namespace TechieDesk.Services.Updates;

/// <summary>
/// Decides whether a newer release exists and fetches it (REQ-FN-038b / BRD-131).
/// </summary>
/// <remarks>
/// <para><b>Why this never installs the update itself.</b> BRD-131 asks the app to "check for and
/// apply" updates. The check, the channel policy and the download are implemented here; replacing the
/// running application is deliberately withheld, and the reason is integrity rather than effort. The
/// packages this app publishes today are UNSIGNED (REQ-FN-038c has no signing identity), so nothing
/// downloaded can be cryptographically attributed to the publisher. An updater that silently replaced
/// the running binary with an unverifiable download would be a remote-code-execution path wearing a
/// feature's clothes — one compromised or spoofed feed response and it executes attacker-supplied
/// code with the user's privileges, automatically, on launch. Downloading over TLS and handing the
/// file to the operator keeps the operating system's own gatekeeping — Gatekeeper, SmartScreen — in
/// the loop. Once 038c signs the artefacts, unattended install becomes defensible because the
/// signature can be verified before anything is executed, and that is the point at which the
/// "background install" switch should start doing something.</para>
/// <para><b>Downloads land in the data directory, never the bundle.</b> REQ-FN-037 made the data
/// directory the single home for state and REQ-FN-034 was the defect where a component held a second
/// opinion about that. A downloaded package is state. Writing it beside the executable would put it
/// inside the read-only <c>.app</c> bundle — the identical mistake found in the Serilog configuration
/// — and would also be discarded by the very update it is meant to install.</para>
/// </remarks>
public sealed class UpdateService : IUpdateService
{
    private readonly IUpdateFeed feed;
    private readonly IAppVersionProvider versionProvider;
    private readonly IUpdatePreferencesStore preferences;
    private readonly HttpClient httpClient;
    private readonly ILogger<UpdateService> logger;
    private readonly string dataDirectory;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="UpdateService"/> class.</summary>
    /// <param name="feed">Release feed.</param>
    /// <param name="versionProvider">The running application's version.</param>
    /// <param name="preferences">Operator update choices.</param>
    /// <param name="httpClient">Client used to fetch the package.</param>
    /// <param name="configuration">Supplies the data-directory override, if any.</param>
    /// <param name="timeProvider">Clock, injected so the check timestamp is testable.</param>
    /// <param name="logger">Diagnostics.</param>
    public UpdateService(
        IUpdateFeed feed,
        IAppVersionProvider versionProvider,
        IUpdatePreferencesStore preferences,
        HttpClient httpClient,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<UpdateService> logger)
    {
        this.feed = feed;
        this.versionProvider = versionProvider;
        this.preferences = preferences;
        this.httpClient = httpClient;
        this.timeProvider = timeProvider;
        this.logger = logger;
        dataDirectory = DataDirectory.Resolve(configuration[DataDirectory.ConfigKey]);
    }

    /// <inheritdoc />
    public string CurrentVersionDisplay => versionProvider.RawVersion;

    /// <summary>Gets the directory downloaded packages are written to.</summary>
    public string DownloadDirectory => Path.Combine(dataDirectory, DataDirectory.DownloadDirectoryName);

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var current = versionProvider.Current;
        var stored = await preferences.LoadAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<AvailableRelease> releases;
        try
        {
            releases = await feed.GetReleasesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (UpdateFeedException ex)
        {
            // REQ-NFR-010: report the failure. Reporting "up to date" here would be indistinguishable
            // from a successful check and would hide an available security fix behind a network blip.
            logger.LogWarning(ex, "Update check failed");
            return new UpdateCheckResult(UpdateCheckStatus.Failed, current, null, null, ex.Message, now);
        }

        var candidates = releases.Where(release => stored.IncludePrerelease || !release.IsPrerelease);

        // A user on the prerelease channel who switches it off must not be told they are up to date
        // while running a beta that is newer than every stable build. Comparing against the newest
        // release the CHANNEL allows, and only offering it when it is strictly newer, means such a
        // user is simply left where they are until stable catches up.
        var newest = candidates
            .OrderByDescending(release => release.Version)
            .FirstOrDefault();

        await RecordCheckedAsync(stored, now, cancellationToken).ConfigureAwait(false);

        if (newest is null || newest.Version <= current)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, current, null, null, null, now);
        }

        var asset = newest.AssetFor(DataDirectory.CurrentPlatform);
        logger.LogInformation(
            "Update available: {Current} -> {Latest} (download {Available})",
            current,
            newest.Version,
            asset is null ? "unavailable for this platform" : "available");

        return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, current, newest, asset, null, now);
    }

    /// <inheritdoc />
    public async Task<string> DownloadAsync(ReleaseAsset asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        Directory.CreateDirectory(DownloadDirectory);

        // The asset name comes from the feed, so it is remote input and must never be able to escape
        // the download directory — "../../TechieDesk.app/Contents/Info.plist" would otherwise be a
        // write into the application bundle courtesy of whoever controls the feed.
        var fileName = Path.GetFileName(asset.Name);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new UpdateFeedException("The update package has no usable file name.");
        }

        var destination = Path.Combine(DownloadDirectory, fileName);
        var partial = destination + ".part";

        try
        {
            using var response = await httpClient
                .GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateFeedException(
                    $"The update package could not be downloaded ({(int)response.StatusCode} {response.ReasonPhrase}).");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(partial))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            TryDelete(partial);
            throw new UpdateFeedException("The update package could not be downloaded.", ex);
        }

        // A truncated download that kept the real file name would sit there looking installable and
        // fail confusingly at install time, so the partial file is only promoted once it is whole.
        var written = new FileInfo(partial).Length;
        if (asset.SizeBytes > 0 && written != asset.SizeBytes)
        {
            TryDelete(partial);
            throw new UpdateFeedException(
                $"The update package downloaded incompletely ({written} of {asset.SizeBytes} bytes).");
        }

        File.Move(partial, destination, overwrite: true);
        logger.LogInformation("Update package downloaded to {Path} ({Bytes} bytes)", destination, written);
        return destination;
    }

    private async Task RecordCheckedAsync(
        UpdatePreferences stored,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            await preferences
                .SaveAsync(stored with { LastCheckedUtc = now }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Recording WHEN a check ran is a convenience. Failing the check itself because that
            // bookkeeping write failed would turn a cosmetic problem into "updates are broken".
            logger.LogWarning(ex, "Could not record the update-check timestamp");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Leaving a stray .part file behind is harmless; it is overwritten on the next attempt.
        }
    }
}
