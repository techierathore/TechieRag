namespace TechieDesk.Services.Setup;

/// <summary>
/// Records and reads first-run wizard completion state, persisted as instance-wide
/// key/value settings (REQ-UI-022/023). The <c>SetupComplete</c> flag is the single
/// source of truth the shell consults to decide whether to route a fresh instance to
/// the <c>/setup</c> wizard.
/// </summary>
public interface ISetupStateService
{
    /// <summary>The InstanceSetting key holding the completion flag.</summary>
    public const string SetupCompleteKey = "SetupComplete";

    /// <summary>The InstanceSetting key holding the chosen setup mode.</summary>
    public const string SetupModeKey = "SetupMode";

    /// <summary>The InstanceSetting key holding the (non-secret) AppManager base URL.</summary>
    public const string AppManagerBaseUrlKey = "AppManagerBaseUrl";

    /// <summary>
    /// Returns <c>true</c> when the <c>SetupComplete</c> flag has been persisted by a
    /// prior wizard run.
    /// </summary>
    Task<bool> IsFlagCompleteAsync();

    /// <summary>
    /// Marks the wizard complete: stores the <c>SetupComplete</c> flag, the chosen mode,
    /// a UTC completion timestamp, and (for AppManager mode) the non-secret base URL.
    /// Secrets (API key/secret, admin password) are never persisted here.
    /// </summary>
    /// <param name="mode">The chosen setup mode ("Offline" or "AppManager").</param>
    /// <param name="appManagerBaseUrl">The AppManager base URL, or null for offline mode.</param>
    Task MarkCompleteAsync(string mode, string? appManagerBaseUrl = null);
}
