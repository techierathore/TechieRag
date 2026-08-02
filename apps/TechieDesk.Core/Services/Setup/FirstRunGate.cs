namespace TechieDesk.Services.Setup;

/// <summary>
/// What the shell should do about first-run setup on this launch (REQ-FN-050).
/// </summary>
public enum FirstRunOutcome
{
    /// <summary>Nothing at all: setup is settled and every route serves normally.</summary>
    None,

    /// <summary>Route to the <c>/setup</c> wizard — this instance has never been through it.</summary>
    ShowWizard,

    /// <summary>
    /// Setup is settled but no LLM provider is configured and the user did not choose that
    /// deliberately: show the dismissible, non-blocking hint. Never a redirect.
    /// </summary>
    ShowProviderHint
}

/// <summary>
/// The facts the first-run decision is taken from (REQ-FN-050).
/// </summary>
/// <remarks>
/// This exists as a value rather than as a pile of parameters so the decision can be exercised
/// exhaustively in unit tests: the shell reads these four facts, this record carries them, and
/// <see cref="FirstRunGate.Decide"/> is the whole of the policy.
/// </remarks>
public sealed record FirstRunContext
{
    /// <summary>Gets a value indicating whether the <c>SetupComplete</c> flag is present.</summary>
    /// <remarks>This is the ONLY input that decides whether the wizard is shown.</remarks>
    public bool SetupComplete { get; init; }

    /// <summary>
    /// Gets the number of workspaces visible to the current user.
    /// </summary>
    /// <remarks>
    /// Carried, reported, and deliberately NOT consulted by <see cref="FirstRunGate.Decide"/>. It is
    /// part of the contract precisely because it used to be the gate: the shell returned early
    /// whenever any workspace existed, and REQ-FN-009 auto-bootstraps a default workspace on first
    /// boot, so the setup flag below that early return was unreachable on every real install and the
    /// wizard never appeared once. <c>ADefaultWorkspaceDoesNotSuppressTheWizard</c> asserts the
    /// decision is invariant under this value so the regression cannot come back unnoticed.
    /// </remarks>
    public int WorkspaceCount { get; init; }

    /// <summary>Gets a value indicating whether an LLM provider is configured right now.</summary>
    public bool ProviderConfigured { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user deliberately chose to stay embedded-only.
    /// See <see cref="SetupCompletionState.ChoseEmbeddedOnly"/>.
    /// </summary>
    public bool ChoseEmbeddedOnly { get; init; }

    /// <summary>Gets a value indicating whether the post-setup provider hint was dismissed.</summary>
    public bool ProviderHintDismissed { get; init; }

    /// <summary>Gets a value indicating whether the current route already IS the wizard.</summary>
    public bool OnSetupRoute { get; init; }
}

/// <summary>
/// The first-run policy: given what is known about a prior wizard run, decide whether to offer
/// setup, hint at a missing provider, or do nothing (REQ-FN-050, BRD-52/53).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a pure static function over a value, living in Core rather than inline in
/// <c>MainLayout.razor</c>. The defect REQ-FN-050 fixes was a single mis-ordered early return in a
/// razor component that no test could reach — the head is a MAUI project the net10.0 test project
/// cannot reference, so the policy has to be OUT of the component to be assertable at all.
/// </para>
/// <para>
/// It is also static on purpose: a new DI registration would have to be added to
/// <c>MauiProgram.cs</c>, and the shell can call a pure function with no registration at all.
/// </para>
/// </remarks>
public static class FirstRunGate
{
    /// <summary>
    /// Decides what the shell does about first-run setup.
    /// </summary>
    /// <param name="context">The facts observed by the shell.</param>
    /// <returns>The action to take.</returns>
    public static FirstRunOutcome Decide(FirstRunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The wizard renders on AuthLayout, so it never re-enters this gate; the route check is
        // belt-and-braces against a redirect loop if that ever changes.
        if (context.OnSetupRoute)
        {
            return FirstRunOutcome.None;
        }

        // Acceptance (1): the FLAG decides. Not the workspace count, not the auth mode, not whether
        // a provider happens to be reachable. An instance that has never recorded a completion has
        // never been offered the choice, whatever else is on disk.
        if (!context.SetupComplete)
        {
            return FirstRunOutcome.ShowWizard;
        }

        // Acceptance (2): past this point the wizard is NEVER shown again. Every branch below is a
        // hint at most. Completion is written by a full finish, by an offline finish and by an
        // explicit skip alike, so all three are one-way doors.
        if (context.ProviderConfigured)
        {
            return FirstRunOutcome.None;
        }

        // Acceptance (4): embedded-only is a COMPLETED outcome, not an unfinished one. Someone who
        // chose to stay offline is not missing a provider — they declined one.
        if (context.ChoseEmbeddedOnly)
        {
            return FirstRunOutcome.None;
        }

        // Acceptance (5): a dismissal is permanent, and what is left is a hint, never a redirect.
        return context.ProviderHintDismissed ? FirstRunOutcome.None : FirstRunOutcome.ShowProviderHint;
    }
}
