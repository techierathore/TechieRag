namespace TechieDesk.Services.Branding;

/// <summary>
/// Reads and writes the white-label branding for this install (REQ-UI-037 / BRD-89).
/// </summary>
public interface IBrandingStore
{
    /// <summary>Loads the stored branding, falling back to <see cref="BrandingSettings.Defaults"/>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The effective branding.</returns>
    Task<BrandingSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the supplied branding.</summary>
    /// <param name="branding">The branding to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when the logo is not an accepted data URI.</exception>
    Task SaveAsync(BrandingSettings branding, CancellationToken cancellationToken = default);
}
