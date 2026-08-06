using Microsoft.Extensions.Options;
using TechieDesk.Services.Data;
using TechieDesk.Services.Updates;
using Xunit;

namespace TechieDesk.Tests.Updates;

/// <summary>
/// REQ-FN-038b: the operator's update choices, and where they live. They are stored in the app
/// database, which sits in the REQ-FN-037 data directory — so they survive the update that replaces
/// the application, which a file inside the bundle would not.
/// </summary>
public sealed class UpdatePreferencesTests
{
    /// <summary>With nothing stored, the configured defaults apply.</summary>
    [Fact]
    public async Task FallsBackToConfiguredDefaults()
    {
        var store = Store(new UpdateOptions { AutoCheckOnLaunch = true, IncludePrerelease = false });

        var preferences = await store.LoadAsync();

        Assert.True(preferences.AutoCheckOnLaunch);
        Assert.False(preferences.IncludePrerelease);
        Assert.Null(preferences.LastCheckedUtc);
    }

    /// <summary>An operator opting out is honoured over the default.</summary>
    [Fact]
    public async Task StoredChoiceOverridesTheDefault()
    {
        var settings = new FakeSettings();
        var store = Store(new UpdateOptions { AutoCheckOnLaunch = true }, settings);

        await store.SaveAsync(new UpdatePreferences(false, true, null));
        var preferences = await store.LoadAsync();

        Assert.False(preferences.AutoCheckOnLaunch);
        Assert.True(preferences.IncludePrerelease);
    }

    /// <summary>
    /// The default is not frozen into the database by merely reading it. If loading persisted the
    /// fallback, changing the shipped default would never reach an existing install.
    /// </summary>
    [Fact]
    public async Task ReadingDoesNotPersistTheDefault()
    {
        var settings = new FakeSettings();
        var store = Store(new UpdateOptions { AutoCheckOnLaunch = true }, settings);

        await store.LoadAsync();

        Assert.Empty(settings.Written);
    }

    /// <summary>The last-checked timestamp round-trips, including its offset.</summary>
    [Fact]
    public async Task RoundTripsTheLastCheckedTimestamp()
    {
        var settings = new FakeSettings();
        var store = Store(new UpdateOptions(), settings);
        var when = new DateTimeOffset(2026, 7, 27, 9, 30, 0, TimeSpan.Zero);

        await store.SaveAsync(new UpdatePreferences(true, false, when));
        var preferences = await store.LoadAsync();

        Assert.Equal(when, preferences.LastCheckedUtc);
    }

    /// <summary>A corrupt stored value degrades to the default rather than throwing.</summary>
    [Fact]
    public async Task IgnoresACorruptStoredValue()
    {
        var settings = new FakeSettings();
        await settings.SetAsync(UpdatePreferencesStore.AutoCheckKey, "not-a-bool");
        await settings.SetAsync(UpdatePreferencesStore.LastCheckedKey, "not-a-date");
        var store = Store(new UpdateOptions { AutoCheckOnLaunch = true }, settings);

        var preferences = await store.LoadAsync();

        Assert.True(preferences.AutoCheckOnLaunch);
        Assert.Null(preferences.LastCheckedUtc);
    }

    private static UpdatePreferencesStore Store(UpdateOptions options, FakeSettings? settings = null) =>
        new(settings ?? new FakeSettings(), Options.Create(options));

    private sealed class FakeSettings : IInstanceSettingRepository
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public List<string> Written { get; } = [];

        public Task<string?> GetAsync(string settingKey) =>
            Task.FromResult(values.TryGetValue(settingKey, out var value) ? value : null);

        public string? Get(string settingKey) =>
            values.TryGetValue(settingKey, out var value) ? value : null;

        public Task SetAsync(string settingKey, string settingValue)
        {
            values[settingKey] = settingValue;
            Written.Add(settingKey);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InstanceSetting>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<InstanceSetting>>([]);
    }
}
