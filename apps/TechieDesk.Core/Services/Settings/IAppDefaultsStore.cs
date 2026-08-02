namespace TechieDesk.Services.Settings;

/// <summary>
/// Reads and writes the app-owned half of the App settings Defaults tab (REQ-UI-028).
/// </summary>
/// <remarks>
/// The LLM, embedding and vector-store defaults already have a home — the TechieRag configuration
/// file — and are not duplicated here. Only the upload ceiling is app-owned, and it lives in the
/// <c>InstanceSetting</c> table so it survives a configuration reset.
/// </remarks>
public interface IAppDefaultsStore
{
    /// <summary>Reads the app-wide upload ceiling in megabytes.</summary>
    /// <returns>
    /// The stored value, or <see cref="AppDefaultsStore.DefaultMaxUploadSizeMb"/> when nothing has
    /// been stored or what is stored is not a usable size.
    /// </returns>
    Task<int> GetMaxUploadSizeMbAsync();

    /// <summary>Stores the app-wide upload ceiling in megabytes.</summary>
    /// <param name="megabytes">The ceiling, between 1 and <see cref="AppDefaultsStore.MaximumMaxUploadSizeMb"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside the permitted range.</exception>
    Task SetMaxUploadSizeMbAsync(int megabytes);
}
