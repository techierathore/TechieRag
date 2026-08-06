using System.Text.Json;
using TechieDesk.Services.Data;

namespace TechieDesk.Services.Scheduling;

/// <summary>Reads and writes the background-service settings (BRD-139).</summary>
public interface ISchedulerPreferencesStore
{
    /// <summary>Loads the saved settings, or the defaults on a fresh install.</summary>
    /// <returns>The settings.</returns>
    Task<SchedulerPreferences> LoadAsync();

    /// <summary>Saves the settings.</summary>
    /// <param name="preferences">The settings to save.</param>
    /// <returns>A task that completes when the settings are written.</returns>
    Task SaveAsync(SchedulerPreferences preferences);
}

/// <summary>
/// Stores the background-service settings as one JSON value in the existing
/// <c>InstanceSetting</c> table (BRD-139).
/// </summary>
/// <remarks>
/// A key/value row rather than a table of its own. This is a single settings record for a
/// single-user install; giving it columns would mean a migration every time BRD-139 grows a
/// run condition, for data nothing ever queries by field.
/// </remarks>
public sealed class SchedulerPreferencesStore : ISchedulerPreferencesStore
{
    /// <summary>The <c>InstanceSetting</c> key these settings live under.</summary>
    public const string SettingKey = "SchedulerPreferences";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IInstanceSettingRepository settings;
    private readonly ILogger<SchedulerPreferencesStore> logger;

    /// <summary>Initializes the store.</summary>
    /// <param name="settings">The instance-setting repository.</param>
    /// <param name="logger">Logger.</param>
    public SchedulerPreferencesStore(
        IInstanceSettingRepository settings, ILogger<SchedulerPreferencesStore> logger)
    {
        this.settings = settings;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<SchedulerPreferences> LoadAsync()
    {
        var stored = await settings.GetAsync(SettingKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(stored))
        {
            return SchedulerPreferences.Default;
        }

        try
        {
            return JsonSerializer.Deserialize<SchedulerPreferences>(stored, SerializerOptions)
                   ?? SchedulerPreferences.Default;
        }
        catch (JsonException exception)
        {
            // Unreadable settings fall back to "nothing restricted" rather than to "nothing runs".
            // The opposite default would silently disable every automation on the machine.
            logger.LogWarning(exception, "Scheduler preferences could not be read; using defaults");
            return SchedulerPreferences.Default;
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(SchedulerPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return settings.SetAsync(SettingKey, JsonSerializer.Serialize(preferences, SerializerOptions));
    }
}
