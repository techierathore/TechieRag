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

    /// <summary>Gets a setting value by key, synchronously.</summary>
    /// <param name="settingKey">The setting key.</param>
    /// <returns>The value, or null when the key does not exist.</returns>
    /// <remarks>
    /// REQ-FN-049. Exists for the ONE caller that has to have an answer before the first frame is
    /// drawn — the composition root reading the stored UI language. Blocking on
    /// <see cref="GetAsync"/> from a platform launch thread is what deadlocked the app, and
    /// "block on the async one, but carefully" is not a thing that survives contact with a
    /// maintainer. SQLite has no asynchronous file I/O anyway, so this is what the async overload
    /// already does underneath, stated honestly. Do NOT reach for it from a component; use
    /// <see cref="GetAsync"/> everywhere else.
    /// </remarks>
    string? Get(string settingKey);

    /// <summary>Creates or updates a setting (upsert) and stamps <c>UpdatedAt</c>.</summary>
    /// <param name="settingKey">The setting key.</param>
    /// <param name="settingValue">The value to store.</param>
    Task SetAsync(string settingKey, string settingValue);

    /// <summary>Lists all settings.</summary>
    /// <returns>All setting rows ordered by key.</returns>
    Task<IReadOnlyList<InstanceSetting>> GetAllAsync();
}
