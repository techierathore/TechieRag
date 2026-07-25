using TechieDesk.Services.Data;

namespace TechieDesk.Services.Setup;

/// <summary>
/// <see cref="IInstanceSettingRepository"/>-backed implementation of
/// <see cref="ISetupStateService"/> (REQ-UI-022/023). Completion state lives in the
/// app database so it survives restarts and is shared across every circuit.
/// </summary>
public sealed class SetupStateService : ISetupStateService
{
    private readonly IInstanceSettingRepository settings;

    /// <summary>Initializes the service.</summary>
    /// <param name="settings">The instance-setting repository.</param>
    public SetupStateService(IInstanceSettingRepository settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc />
    public async Task<bool> IsFlagCompleteAsync()
    {
        var value = await settings.GetAsync(ISetupStateService.SetupCompleteKey).ConfigureAwait(false);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task MarkCompleteAsync(string mode, string? appManagerBaseUrl = null)
    {
        await settings.SetAsync(ISetupStateService.SetupCompleteKey, "true").ConfigureAwait(false);
        await settings.SetAsync(ISetupStateService.SetupModeKey,
            string.IsNullOrWhiteSpace(mode) ? "Offline" : mode.Trim()).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(appManagerBaseUrl))
        {
            await settings.SetAsync(ISetupStateService.AppManagerBaseUrlKey, appManagerBaseUrl.Trim())
                .ConfigureAwait(false);
        }
    }
}
