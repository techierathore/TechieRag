namespace TechieDesk.Services.Setup;

/// <summary>
/// Everything the shell needs to know about a prior first-run wizard run, read in one go
/// (REQ-FN-050). Reading the flag, the mode, the recorded provider and the hint dismissal as a
/// single snapshot keeps the shell from making four settings round-trips on every launch, and —
/// more importantly — keeps the four values consistent with each other while a decision is taken.
/// </summary>
/// <param name="Complete">
/// True when the <c>SetupComplete</c> flag was written by a prior run — by a full finish, by an
/// offline/embedded-only finish, or by an explicit skip. All three are settled outcomes.
/// </param>
/// <param name="Mode">The recorded mode: <c>Offline</c>, <c>AppManager</c> or <c>Skipped</c>.</param>
/// <param name="Provider">
/// The LLM provider the user chose at setup, or <see cref="ISetupStateService.NoProvider"/> when
/// they deliberately stayed embedded-only. Empty when the wizard predates this record.
/// </param>
/// <param name="ProviderHintDismissed">
/// True once the user has dismissed the post-setup "no AI provider" hint. A dismissal is permanent:
/// the whole point of REQ-FN-050 is that a choice already made is never put to the user again.
/// </param>
public sealed record SetupCompletionState(
    bool Complete,
    string Mode,
    string Provider,
    bool ProviderHintDismissed)
{
    /// <summary>An instance that has never been through the wizard.</summary>
    public static SetupCompletionState NeverRun { get; } =
        new(false, string.Empty, string.Empty, false);

    /// <summary>
    /// Gets a value indicating whether the user DELIBERATELY chose to stay embedded-only — either by
    /// picking "skip, embedded-only" on the provider step or by skipping the wizard outright.
    /// </summary>
    /// <remarks>
    /// This is the distinction REQ-FN-050 acceptance (4) turns on. "No provider configured" and "the
    /// user decided not to configure a provider" look identical in the RAG config, and treating the
    /// second as an unfinished state is exactly the nagging the owner asked to be rid of.
    /// </remarks>
    public bool ChoseEmbeddedOnly =>
        string.Equals(Provider, ISetupStateService.NoProvider, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Mode, ISetupStateService.SkippedMode, StringComparison.OrdinalIgnoreCase);
}
