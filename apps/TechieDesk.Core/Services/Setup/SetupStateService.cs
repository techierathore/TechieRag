using TechieDesk.Services.Data;

namespace TechieDesk.Services.Setup;

/// <summary>
/// <see cref="IInstanceSettingRepository"/>-backed implementation of
/// <see cref="ISetupStateService"/> (REQ-UI-022/023, REQ-FN-050). Completion state lives in the
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
        return IsTrue(value);
    }

    /// <inheritdoc />
    public async Task<SetupCompletionState> ReadAsync()
    {
        var complete = await settings.GetAsync(ISetupStateService.SetupCompleteKey).ConfigureAwait(false);
        if (!IsTrue(complete))
        {
            return SetupCompletionState.NeverRun;
        }

        var mode = await settings.GetAsync(ISetupStateService.SetupModeKey).ConfigureAwait(false);
        var provider = await settings.GetAsync(ISetupStateService.SetupProviderKey).ConfigureAwait(false);
        var dismissed = await settings.GetAsync(ISetupStateService.ProviderHintDismissedKey)
            .ConfigureAwait(false);

        return new SetupCompletionState(
            true,
            mode ?? string.Empty,
            provider ?? string.Empty,
            IsTrue(dismissed));
    }

    /// <inheritdoc />
    public async Task MarkCompleteAsync(
        string mode,
        string? appManagerBaseUrl = null,
        string? provider = null)
    {
        await settings.SetAsync(ISetupStateService.SetupCompleteKey, "true").ConfigureAwait(false);
        await settings.SetAsync(ISetupStateService.SetupModeKey,
            string.IsNullOrWhiteSpace(mode) ? "Offline" : mode.Trim()).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(provider))
        {
            await settings.SetAsync(ISetupStateService.SetupProviderKey, provider.Trim())
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(appManagerBaseUrl))
        {
            await settings.SetAsync(ISetupStateService.AppManagerBaseUrlKey, appManagerBaseUrl.Trim())
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task MarkSkippedAsync() =>
        MarkCompleteAsync(ISetupStateService.SkippedMode, null, ISetupStateService.NoProvider);

    /// <inheritdoc />
    public Task DismissProviderHintAsync() =>
        settings.SetAsync(ISetupStateService.ProviderHintDismissedKey, "true");

    /// <summary>Reads a persisted boolean the way every other flag in this store is written.</summary>
    /// <param name="value">The stored value, possibly null.</param>
    /// <returns>True when the value is the string "true", case-insensitively.</returns>
    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
