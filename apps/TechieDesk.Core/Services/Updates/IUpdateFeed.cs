namespace TechieDesk.Services.Updates;

/// <summary>
/// Reads the list of published desktop releases (REQ-FN-038b).
/// </summary>
/// <remarks>
/// Separated from <see cref="IUpdateService"/> so the decision logic — is this newer, does the
/// channel allow it, which asset installs it — is testable without a network, and so a fork can
/// substitute a different feed without touching that logic.
/// </remarks>
public interface IUpdateFeed
{
    /// <summary>Fetches published releases, newest first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Releases the feed advertises for the desktop app.</returns>
    /// <exception cref="UpdateFeedException">The feed could not be read.</exception>
    Task<IReadOnlyList<AvailableRelease>> GetReleasesAsync(CancellationToken cancellationToken = default);
}
