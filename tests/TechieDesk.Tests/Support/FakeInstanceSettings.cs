using TechieDesk.Services.Data;

namespace TechieDesk.Tests.Support;

/// <summary>
/// In-memory <see cref="IInstanceSettingRepository"/> for the settings-store tests.
/// </summary>
/// <remarks>
/// Records the keys that were WRITTEN as well as the values, because several of these tests are
/// about what a store does NOT persist — a default that gets frozen into the database on first read
/// is invisible if you only inspect what comes back out.
/// </remarks>
public sealed class FakeInstanceSettings : IInstanceSettingRepository
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    /// <summary>Gets the keys written, in order, including repeats.</summary>
    public List<string> Written { get; } = [];

    /// <summary>Seeds a value without recording it as a write.</summary>
    /// <param name="settingKey">The setting key.</param>
    /// <param name="settingValue">The value to seed.</param>
    public void Seed(string settingKey, string settingValue) => values[settingKey] = settingValue;

    /// <inheritdoc />
    public Task<string?> GetAsync(string settingKey) =>
        Task.FromResult(values.TryGetValue(settingKey, out var value) ? value : null);

    /// <inheritdoc />
    public string? Get(string settingKey) =>
        values.TryGetValue(settingKey, out var value) ? value : null;

    /// <inheritdoc />
    public Task SetAsync(string settingKey, string settingValue)
    {
        values[settingKey] = settingValue;
        Written.Add(settingKey);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InstanceSetting>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<InstanceSetting>>(
            values.Select(pair => new InstanceSetting
            {
                SettingKey = pair.Key,
                SettingValue = pair.Value,
                UpdatedAt = DateTime.UtcNow
            }).ToList());
}
