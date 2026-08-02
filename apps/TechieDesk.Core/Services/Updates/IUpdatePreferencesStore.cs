namespace TechieDesk.Services.Updates;

/// <summary>
/// Reads and writes the operator's update choices (REQ-FN-038b).
/// </summary>
public interface IUpdatePreferencesStore
{
    /// <summary>Loads the stored preferences, falling back to the configured defaults.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The effective preferences for this install.</returns>
    Task<UpdatePreferences> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the operator's choices.</summary>
    /// <param name="preferences">The preferences to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(UpdatePreferences preferences, CancellationToken cancellationToken = default);
}
