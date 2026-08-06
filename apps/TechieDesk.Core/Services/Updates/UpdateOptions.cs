namespace TechieDesk.Services.Updates;

/// <summary>
/// Where the update feed lives and how it is queried (REQ-FN-038b).
/// </summary>
/// <remarks>
/// Bound from the <c>Updates</c> configuration section so an operator can point a fork or a mirror
/// at its own releases without a rebuild. Nothing here is a secret: the feed is a public releases
/// endpoint, and the update check deliberately sends no credentials — see <c>GitHubReleaseFeed</c>.
/// </remarks>
public sealed class UpdateOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SectionName = "Updates";

    /// <summary>Gets or sets the account that owns the releases repository.</summary>
    public string RepositoryOwner { get; set; } = "techierathore";

    /// <summary>Gets or sets the releases repository name.</summary>
    public string RepositoryName { get; set; } = "TechieRag";

    /// <summary>Gets or sets the API base address for the releases feed.</summary>
    public string ApiBaseAddress { get; set; } = "https://api.github.com/";

    /// <summary>Gets or sets how many releases to consider on each check.</summary>
    public int PageSize { get; set; } = 30;

    /// <summary>Gets or sets whether the app checks for updates on its own at launch.</summary>
    /// <remarks>
    /// <para><b>Defaults to FALSE, deliberately, against the usual convention.</b> REQ-NFR-008 /
    /// BRD-99 state that nothing leaves this instance except calls to the operator's own configured
    /// providers and to AppManager, and <c>OutboundEgressTests</c> enforces a zero-egress default.
    /// An update check contacts a third party the operator never configured, so switching it on by
    /// default would make an offline-first product phone out at every launch without being asked —
    /// the exact behaviour that NFR exists to prevent. Checking is therefore opt-in, and the manual
    /// "Check for updates" button is always available regardless.</para>
    /// <para>The trade-off is real and is the operator's to make: with this off, a published security
    /// fix is not announced until someone looks. Flipping the default is a one-line change here, but
    /// it belongs with a decision on REQ-NFR-008 rather than as a quiet convenience.</para>
    /// <para>The operator's own choice is stored per install and overrides this — see
    /// <c>IUpdatePreferencesStore</c>.</para>
    /// </remarks>
    public bool AutoCheckOnLaunch { get; set; }

    /// <summary>Gets or sets whether prerelease builds are offered by default.</summary>
    public bool IncludePrerelease { get; set; }

    /// <summary>Gets the resolved releases endpoint.</summary>
    /// <returns>A relative URI for the releases list.</returns>
    public string ReleasesPath() =>
        $"repos/{RepositoryOwner}/{RepositoryName}/releases?per_page={PageSize}";
}
