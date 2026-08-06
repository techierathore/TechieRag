using TechieDesk.Services.Data;

namespace TechieDesk.Services.Branding;

/// <summary>
/// Stores white-label branding in the app database (REQ-UI-037 / BRD-89).
/// </summary>
/// <remarks>
/// Same store of record and same reasoning as <c>AppearanceStore</c>: the
/// <see cref="IInstanceSettingRepository"/> table lives in the REQ-FN-037 per-user data directory,
/// so branding survives an update and nothing is written into the read-only application bundle.
/// </remarks>
public sealed class BrandingStore : IBrandingStore
{
    /// <summary>Setting key for the product name.</summary>
    public const string ProductNameKey = "BrandingProductName";

    /// <summary>Setting key for the welcome message.</summary>
    public const string WelcomeMessageKey = "BrandingWelcomeMessage";

    /// <summary>Setting key for the footer links.</summary>
    public const string FooterLinksKey = "BrandingFooterLinks";

    /// <summary>Setting key for the logo data URI.</summary>
    public const string LogoKey = "BrandingLogoDataUri";

    private readonly IInstanceSettingRepository settings;

    /// <summary>Initializes a new instance of the <see cref="BrandingStore"/> class.</summary>
    /// <param name="settings">Instance-setting persistence.</param>
    public BrandingStore(IInstanceSettingRepository settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc />
    public async Task<BrandingSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var productName = await settings.GetAsync(ProductNameKey).ConfigureAwait(false);
        var welcome = await settings.GetAsync(WelcomeMessageKey).ConfigureAwait(false);
        var footer = await settings.GetAsync(FooterLinksKey).ConfigureAwait(false);
        var logo = await settings.GetAsync(LogoKey).ConfigureAwait(false);

        // A blank product name would leave the shell with no lockup text at all, so an empty stored
        // value is treated exactly like a missing one.
        return new BrandingSettings(
            string.IsNullOrWhiteSpace(productName)
                ? BrandingSettings.DefaultProductName
                : productName,
            welcome ?? BrandingSettings.DefaultWelcomeMessage,
            footer ?? BrandingSettings.DefaultFooterLinks,

            // Re-checked on the way out: the row is plain text and its value goes into an image
            // source. See BrandingLogo.IsAcceptable.
            BrandingLogo.IsAcceptable(logo) && !string.IsNullOrWhiteSpace(logo) ? logo : null);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        BrandingSettings branding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branding);

        if (!BrandingLogo.IsAcceptable(branding.LogoDataUri))
        {
            throw new ArgumentException(
                "The logo must be an SVG or PNG data URI.", nameof(branding));
        }

        var productName = string.IsNullOrWhiteSpace(branding.ProductName)
            ? BrandingSettings.DefaultProductName
            : branding.ProductName.Trim();

        await settings.SetAsync(ProductNameKey, productName).ConfigureAwait(false);
        await settings.SetAsync(WelcomeMessageKey, branding.WelcomeMessage ?? string.Empty)
            .ConfigureAwait(false);
        await settings.SetAsync(FooterLinksKey, branding.FooterLinks ?? string.Empty)
            .ConfigureAwait(false);
        await settings.SetAsync(LogoKey, branding.LogoDataUri ?? string.Empty).ConfigureAwait(false);
    }
}
