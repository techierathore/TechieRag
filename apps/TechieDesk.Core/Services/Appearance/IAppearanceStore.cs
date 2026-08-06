namespace TechieDesk.Services.Appearance;

/// <summary>
/// Reads and writes the operator's appearance choices (REQ-UI-038 / BRD-90).
/// </summary>
public interface IAppearanceStore
{
    /// <summary>Loads the stored choices, falling back to <see cref="AppearanceSettings.Defaults"/>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The effective appearance settings.</returns>
    Task<AppearanceSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the supplied choices.</summary>
    /// <param name="settings">The choices to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(AppearanceSettings settings, CancellationToken cancellationToken = default);
}
