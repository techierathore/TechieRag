using System.Globalization;
using TechieDesk.Services.Data;

namespace TechieDesk.Services.Settings;

/// <summary>
/// <see cref="IAppDefaultsStore"/> over the Dapper-backed <c>InstanceSetting</c> table (BRD-102).
/// </summary>
public sealed class AppDefaultsStore : IAppDefaultsStore
{
    /// <summary>The <c>InstanceSetting</c> key holding the upload ceiling.</summary>
    public const string MaxUploadSizeKey = "Defaults.MaxUploadSizeMb";

    /// <summary>The ceiling applied when nothing usable has been stored.</summary>
    public const int DefaultMaxUploadSizeMb = 50;

    /// <summary>The largest ceiling the screen will accept.</summary>
    public const int MaximumMaxUploadSizeMb = 2048;

    private readonly IInstanceSettingRepository settings;

    /// <summary>Initializes the store.</summary>
    /// <param name="settings">The instance-setting repository backing the value.</param>
    public AppDefaultsStore(IInstanceSettingRepository settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc />
    public async Task<int> GetMaxUploadSizeMbAsync()
    {
        var stored = await settings.GetAsync(MaxUploadSizeKey).ConfigureAwait(false);

        // A row hand-edited to "unlimited", to an empty string, or to a negative number is not a
        // size. Falling back to the shipped default is safe; trusting it is not.
        if (!int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var megabytes)
            || megabytes < 1
            || megabytes > MaximumMaxUploadSizeMb)
        {
            return DefaultMaxUploadSizeMb;
        }

        return megabytes;
    }

    /// <inheritdoc />
    public Task SetMaxUploadSizeMbAsync(int megabytes)
    {
        if (megabytes < 1 || megabytes > MaximumMaxUploadSizeMb)
        {
            throw new ArgumentOutOfRangeException(
                nameof(megabytes),
                megabytes,
                $"The upload ceiling must be between 1 and {MaximumMaxUploadSizeMb} MB.");
        }

        return settings.SetAsync(
            MaxUploadSizeKey, megabytes.ToString(CultureInfo.InvariantCulture));
    }
}
