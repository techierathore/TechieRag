using TechieDesk.Services.Appearance;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Appearance;

/// <summary>
/// REQ-UI-038 (BRD-90): the theme and accent choice, and where it lives. The app database sits in
/// the REQ-FN-037 per-user data directory, so a chosen theme survives the update that replaces the
/// application — which anything written inside the read-only .app bundle would not.
/// </summary>
public sealed class AppearanceStoreTests
{
    /// <summary>With nothing stored, the shipped defaults apply.</summary>
    [Fact]
    public async Task FallsBackToTheShippedDefaults()
    {
        var store = new AppearanceStore(new FakeInstanceSettings());

        var settings = await store.LoadAsync();

        Assert.Equal(ThemeMode.System, settings.Mode);
        Assert.Equal(AccentPalette.DefaultKey, settings.AccentKey);
    }

    /// <summary>
    /// Every mode the Appearance panel offers survives the round trip through the database.
    /// </summary>
    /// <param name="mode">The mode the operator chose.</param>
    /// <remarks>
    /// <see cref="ThemeMode.System"/> is the one that matters here. It is stored as its own value
    /// rather than resolved to light or dark at save time, so "match system" keeps FOLLOWING the OS
    /// after a restart instead of freezing whatever the machine happened to be when it was chosen.
    /// A store that collapsed it would still pass <c>RoundTripsTheChoice</c>, which only exercises
    /// one mode.
    /// </remarks>
    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    [InlineData(ThemeMode.System)]
    public async Task RoundTripsEveryOfferedMode(ThemeMode mode)
    {
        var settings = new FakeInstanceSettings();
        var store = new AppearanceStore(settings);

        await store.SaveAsync(new AppearanceSettings(mode, AccentPalette.DefaultKey));

        Assert.Equal(mode, (await store.LoadAsync()).Mode);
    }

    /// <summary>
    /// Every accent the swatch row offers round-trips by key. The panel writes the palette key
    /// verbatim, so an accent whose key the store normalised away would silently snap back to indigo
    /// the next time the window opened.
    /// </summary>
    [Fact]
    public async Task RoundTripsEveryOfferedAccent()
    {
        foreach (var accent in AccentPalette.All)
        {
            var settings = new FakeInstanceSettings();
            var store = new AppearanceStore(settings);

            await store.SaveAsync(new AppearanceSettings(ThemeMode.System, accent.Key));
            var reloaded = await store.LoadAsync();

            Assert.Equal(accent.Key, reloaded.AccentKey);
            Assert.Equal(accent, reloaded.Accent);
        }
    }

    /// <summary>A chosen mode and accent round-trip.</summary>
    [Fact]
    public async Task RoundTripsTheChoice()
    {
        var settings = new FakeInstanceSettings();
        var store = new AppearanceStore(settings);

        await store.SaveAsync(new AppearanceSettings(ThemeMode.Dark, "green"));
        var reloaded = await store.LoadAsync();

        Assert.Equal(ThemeMode.Dark, reloaded.Mode);
        Assert.Equal("green", reloaded.AccentKey);
    }

    /// <summary>
    /// Reading does not persist the fallback. If it did, the shipped default could never be changed
    /// for an install that had merely been opened once.
    /// </summary>
    [Fact]
    public async Task ReadingDoesNotPersistTheDefault()
    {
        var settings = new FakeInstanceSettings();
        var store = new AppearanceStore(settings);

        await store.LoadAsync();

        Assert.Empty(settings.Written);
    }

    /// <summary>
    /// A stored mode this version does not recognise degrades to the default. The row can have been
    /// written by a newer build, and refusing to paint is not an option.
    /// </summary>
    [Fact]
    public async Task IgnoresAnUnknownStoredMode()
    {
        var settings = new FakeInstanceSettings();
        settings.Seed(AppearanceStore.ThemeModeKey, "Sepia");
        var store = new AppearanceStore(settings);

        var loaded = await store.LoadAsync();

        Assert.Equal(AppearanceSettings.Defaults.Mode, loaded.Mode);
    }

    /// <summary>An unknown accent key degrades to the product accent rather than to no accent.</summary>
    [Fact]
    public async Task IgnoresAnUnknownStoredAccent()
    {
        var settings = new FakeInstanceSettings();
        settings.Seed(AppearanceStore.AccentKey, "chartreuse");
        var store = new AppearanceStore(settings);

        var loaded = await store.LoadAsync();

        Assert.Equal(AccentPalette.DefaultKey, loaded.AccentKey);
    }

    /// <summary>The mode is stored case-insensitively, so a hand-edited row still loads.</summary>
    [Fact]
    public async Task ReadsAStoredModeCaseInsensitively()
    {
        var settings = new FakeInstanceSettings();
        settings.Seed(AppearanceStore.ThemeModeKey, "dark");
        var store = new AppearanceStore(settings);

        var loaded = await store.LoadAsync();

        Assert.Equal(ThemeMode.Dark, loaded.Mode);
    }

    /// <summary>Saving normalises the accent key, so the database never holds an unknown value.</summary>
    [Fact]
    public async Task NormalisesTheAccentOnSave()
    {
        var settings = new FakeInstanceSettings();
        var store = new AppearanceStore(settings);

        await store.SaveAsync(new AppearanceSettings(ThemeMode.Light, "not-a-colour"));
        var reloaded = await store.LoadAsync();

        Assert.Equal(AccentPalette.DefaultKey, reloaded.AccentKey);
    }
}
