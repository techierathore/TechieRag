namespace TechieDesk.Services.Data;

/// <summary>
/// Get/set access to instance-wide key/value settings (Dapper-only, BRD-102).
/// </summary>
public interface IInstanceSettingRepository
{
    /// <summary>Gets a setting value by key.</summary>
    /// <param name="settingKey">The setting key.</param>
    /// <returns>The value, or null when the key does not exist.</returns>
    Task<string?> GetAsync(string settingKey);

    /// <summary>Creates or updates a setting (upsert) and stamps <c>UpdatedAt</c>.</summary>
    /// <param name="settingKey">The setting key.</param>
    /// <param name="settingValue">The value to store.</param>
    Task SetAsync(string settingKey, string settingValue);

    /// <summary>Lists all settings.</summary>
    /// <returns>All setting rows ordered by key.</returns>
    Task<IReadOnlyList<InstanceSetting>> GetAllAsync();
}
