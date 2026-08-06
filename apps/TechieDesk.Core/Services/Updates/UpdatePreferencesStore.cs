using System.Globalization;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Data;

namespace TechieDesk.Services.Updates;

/// <summary>
/// Stores update preferences in the app database (REQ-FN-038b).
/// </summary>
/// <remarks>
/// <para>Reuses <see cref="IInstanceSettingRepository"/> rather than introducing another settings
/// file. That is not only about tidiness: the app database already lives in the data directory that
/// REQ-FN-037 established, so these choices are automatically preserved across an update and
/// automatically excluded from the application bundle. A new JSON file next to the executable would
/// have been both a second authority on where state lives and a write into a read-only bundle.</para>
/// <para>A missing row means "never chosen", so the configured default applies — which is what makes
/// <see cref="UpdateOptions.AutoCheckOnLaunch"/> a genuine default rather than a value that gets
/// frozen into the database on first read.</para>
/// </remarks>
public sealed class UpdatePreferencesStore : IUpdatePreferencesStore
{
    /// <summary>Setting key for the auto-check choice.</summary>
    public const string AutoCheckKey = "UpdatesAutoCheckOnLaunch";

    /// <summary>Setting key for the prerelease-channel choice.</summary>
    public const string PrereleaseKey = "UpdatesIncludePrerelease";

    /// <summary>Setting key for the last completed check.</summary>
    public const string LastCheckedKey = "UpdatesLastCheckedUtc";

    private readonly IInstanceSettingRepository settings;
    private readonly UpdateOptions options;

    /// <summary>Initializes a new instance of the <see cref="UpdatePreferencesStore"/> class.</summary>
    /// <param name="settings">Instance-setting persistence.</param>
    /// <param name="options">Configured defaults.</param>
    public UpdatePreferencesStore(IInstanceSettingRepository settings, IOptions<UpdateOptions> options)
    {
        this.settings = settings;
        this.options = options.Value;
    }

    /// <inheritdoc />
    public async Task<UpdatePreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        var autoCheck = await ReadBoolAsync(AutoCheckKey, options.AutoCheckOnLaunch).ConfigureAwait(false);
        var prerelease = await ReadBoolAsync(PrereleaseKey, options.IncludePrerelease).ConfigureAwait(false);
        var lastChecked = await ReadTimestampAsync(LastCheckedKey).ConfigureAwait(false);

        return new UpdatePreferences(autoCheck, prerelease, lastChecked);
    }

    /// <inheritdoc />
    public async Task SaveAsync(UpdatePreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        await settings.SetAsync(AutoCheckKey, preferences.AutoCheckOnLaunch ? "true" : "false")
            .ConfigureAwait(false);
        await settings.SetAsync(PrereleaseKey, preferences.IncludePrerelease ? "true" : "false")
            .ConfigureAwait(false);

        if (preferences.LastCheckedUtc is { } checkedAt)
        {
            await settings
                .SetAsync(LastCheckedKey, checkedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> ReadBoolAsync(string key, bool fallback)
    {
        var stored = await settings.GetAsync(key).ConfigureAwait(false);
        return bool.TryParse(stored, out var value) ? value : fallback;
    }

    private async Task<DateTimeOffset?> ReadTimestampAsync(string key)
    {
        var stored = await settings.GetAsync(key).ConfigureAwait(false);
        return DateTimeOffset.TryParse(
            stored,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var value)
            ? value
            : null;
    }
}
