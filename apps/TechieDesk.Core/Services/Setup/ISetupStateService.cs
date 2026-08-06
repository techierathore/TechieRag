namespace TechieDesk.Services.Setup;

/// <summary>
/// Records and reads first-run wizard completion state, persisted as instance-wide
/// key/value settings (REQ-UI-022/023, REQ-FN-050). The <c>SetupComplete</c> flag is the single
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
    /// The InstanceSetting key holding the LLM provider chosen at setup (REQ-FN-050). Written so a
    /// deliberate embedded-only choice can be told apart from an install that simply has no
    /// provider — the two are indistinguishable in the RAG config alone.
    /// </summary>
    public const string SetupProviderKey = "SetupProvider";

    /// <summary>
    /// The InstanceSetting key recording that the post-setup "no AI provider" hint was dismissed
    /// (REQ-FN-050). Persisted rather than held per-circuit so the hint cannot come back on the
    /// next launch.
    /// </summary>
    public const string ProviderHintDismissedKey = "ProviderHintDismissed";

    /// <summary>The <see cref="SetupProviderKey"/> value meaning "deliberately embedded-only".</summary>
    public const string NoProvider = "None";

    /// <summary>The <see cref="SetupModeKey"/> value meaning "the wizard was skipped outright".</summary>
    public const string SkippedMode = "Skipped";

    /// <summary>
    /// Returns <c>true</c> when the <c>SetupComplete</c> flag has been persisted by a
    /// prior wizard run.
    /// </summary>
    Task<bool> IsFlagCompleteAsync();

    /// <summary>
    /// Reads the completion flag, the recorded mode and provider, and the hint dismissal in one
    /// pass (REQ-FN-050).
    /// </summary>
    /// <returns>The snapshot, or <see cref="SetupCompletionState.NeverRun"/> for a fresh instance.</returns>
    Task<SetupCompletionState> ReadAsync();

    /// <summary>
    /// Marks the wizard complete: stores the <c>SetupComplete</c> flag, the chosen mode, the chosen
    /// provider and (for AppManager mode) the non-secret base URL. Secrets (API key/secret, admin
    /// password) are never persisted here.
    /// </summary>
    /// <param name="mode">The chosen setup mode ("Offline", "AppManager" or "Skipped").</param>
    /// <param name="appManagerBaseUrl">The AppManager base URL, or null for offline mode.</param>
    /// <param name="provider">
    /// The chosen LLM provider, or <see cref="NoProvider"/> when the user deliberately stayed
    /// embedded-only. Null leaves any previously recorded provider untouched.
    /// </param>
    Task MarkCompleteAsync(string mode, string? appManagerBaseUrl = null, string? provider = null);

    /// <summary>
    /// Records that the user dismissed the wizard without configuring anything (REQ-FN-050).
    /// </summary>
    /// <remarks>
    /// A skip is a SETTLED outcome, not a postponement: it writes the same completion flag a finish
    /// does, because the owner's requirement is that a user who has already answered the question is
    /// never asked it again.
    /// </remarks>
    Task MarkSkippedAsync();

    /// <summary>
    /// Records that the post-setup "no AI provider" hint was dismissed, permanently (REQ-FN-050).
    /// </summary>
    Task DismissProviderHintAsync();
}
