namespace TechieDesk.Services.Localization;

/// <summary>
/// Reads and writes the chosen UI language (REQ-UI-039 / BRD-91).
/// </summary>
public interface ILanguageStore
{
    /// <summary>
    /// Loads the chosen language, falling back to the operating system's language when it is one
    /// TechieDesk ships, and to English otherwise.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The language to render in.</returns>
    Task<AppLanguage> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the chosen language synchronously, with the same fallbacks as
    /// <see cref="LoadAsync"/>.
    /// </summary>
    /// <returns>The language to render in.</returns>
    /// <remarks>
    /// REQ-FN-049. The composition root has to apply the culture before anything renders —
    /// <c>IStringLocalizer</c> reads <c>CultureInfo.CurrentUICulture</c> at the moment of each
    /// lookup, so a culture applied after first paint leaves the launch screen in English — and it
    /// must do that WITHOUT blocking on a task, which is what hung the app on the UIKit launch
    /// thread. This is the only supported way to get an answer on the launch path; components use
    /// <see cref="LoadAsync"/>.
    /// </remarks>
    AppLanguage Load();

    /// <summary>Persists the chosen language.</summary>
    /// <param name="language">The language to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(AppLanguage language, CancellationToken cancellationToken = default);
}
