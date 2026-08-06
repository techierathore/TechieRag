namespace TechieDesk.Services.Updates;

/// <summary>
/// Checks for and downloads application updates (REQ-FN-038b / BRD-131).
/// </summary>
/// <remarks>
/// <b>This deliberately does not install anything.</b> See <c>UpdateService</c> for why unattended
/// replacement of the running application is withheld until REQ-FN-038c supplies a signing identity.
/// </remarks>
public interface IUpdateService
{
    /// <summary>Gets the version of the running application, for display.</summary>
    string CurrentVersionDisplay { get; }

    /// <summary>Checks the feed for a newer release.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the check concluded. A failure is reported, never disguised as up to date.</returns>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads an update package into the data directory.</summary>
    /// <param name="asset">The asset to download, from a prior <see cref="CheckAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The downloaded file's full path.</returns>
    /// <exception cref="UpdateFeedException">The download failed or was incomplete.</exception>
    Task<string> DownloadAsync(ReleaseAsset asset, CancellationToken cancellationToken = default);
}
