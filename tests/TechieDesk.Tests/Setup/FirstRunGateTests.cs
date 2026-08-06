using TechieDesk.Services.Setup;
using Xunit;

namespace TechieDesk.Tests.Setup;

/// <summary>
/// REQ-FN-050 (BRD-52/53): the first-run policy — who is offered the wizard, who is never offered
/// it again, and who is left alone entirely.
/// </summary>
/// <remarks>
/// <para>
/// The defect these tests exist for was one mis-ordered early return: <c>MainLayout</c> returned as
/// soon as any workspace existed, ABOVE the setup-flag check, and REQ-FN-009 auto-bootstraps a
/// default workspace on first boot — so on every real install the flag check was dead code and the
/// wizard was never shown to anyone. <see cref="ADefaultWorkspaceDoesNotSuppressTheWizard"/> is the
/// direct regression test for it.
/// </para>
/// <para>
/// The policy is asserted here rather than through the component because the head is a MAUI project
/// this net10.0 test project cannot reference. That is exactly why the policy was moved OUT of the
/// razor file and into <see cref="FirstRunGate"/>: a decision no test can reach is a decision that
/// can be wrong for months.
/// </para>
/// </remarks>
public class FirstRunGateTests
{
    /// <summary>
    /// 🔴 The regression test. A fresh install WITH the auto-bootstrapped default workspace present
    /// is still offered the wizard, because the completion FLAG decides and nothing else does.
    /// </summary>
    [Fact]
    public void RoutesToSetupOnAFreshInstallThatAlreadyHasADefaultWorkspace()
    {
        var outcome = FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = false,
            WorkspaceCount = 1
        });

        Assert.Equal(FirstRunOutcome.ShowWizard, outcome);
    }

    /// <summary>
    /// The decision is INVARIANT under the workspace count. This is the property the old code broke:
    /// it is not enough that one workspace no longer suppresses setup, no number of them may.
    /// </summary>
    /// <param name="workspaceCount">The number of workspaces the shell can see.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(37)]
    public void ADefaultWorkspaceDoesNotSuppressTheWizard(int workspaceCount)
    {
        var outcome = FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = false,
            WorkspaceCount = workspaceCount
        });

        Assert.Equal(FirstRunOutcome.ShowWizard, outcome);
    }

    /// <summary>
    /// 🔴 The mirror-image regression, introduced by REQ-FN-049: the REQ-FN-009 default-workspace
    /// bootstrap now runs in BACKGROUND initialization, so the shell can legitimately observe zero
    /// workspaces on a mature install whose data simply has not loaded yet. A completed install must
    /// not be thrown back into the wizard because of that race.
    /// </summary>
    /// <remarks>
    /// This is the same defect as the original one seen from the other side. Both come from letting
    /// a timing-dependent observation stand in for a recorded decision, and the fix for both is that
    /// only the flag decides — which is why the workspace count is asserted irrelevant in BOTH
    /// directions rather than only in the one the owner happened to hit.
    /// </remarks>
    /// <param name="workspaceCount">The number of workspaces visible at guard time.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    public void DoesNotReopenTheWizardWhileBackgroundInitializationIsStillLoadingWorkspaces(
        int workspaceCount)
    {
        var outcome = FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = true,
            WorkspaceCount = workspaceCount,
            ProviderConfigured = true
        });

        Assert.Equal(FirstRunOutcome.None, outcome);
    }

    /// <summary>The wizard does not redirect to itself.</summary>
    [Fact]
    public void DoesNothingWhileTheWizardIsAlreadyOnScreen()
    {
        var outcome = FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = false,
            WorkspaceCount = 1,
            OnSetupRoute = true
        });

        Assert.Equal(FirstRunOutcome.None, outcome);
    }

    /// <summary>
    /// 🔴 Once setup is settled the wizard is NEVER offered again — whatever else is true of the
    /// instance. Asserted across the whole input space rather than on a handful of cases, because
    /// "it came back under some combination nobody enumerated" is precisely the failure the owner
    /// described ("why the hell do I have to set it up every time when I have done it once").
    /// </summary>
    [Fact]
    public void NeverShowsTheWizardAgainOnceSetupIsComplete()
    {
        foreach (var context in EveryCompletedCombination())
        {
            Assert.NotEqual(FirstRunOutcome.ShowWizard, FirstRunGate.Decide(context));
        }
    }

    /// <summary>
    /// 🔴 Choosing embedded-only is a COMPLETED outcome. A user who deliberately stayed offline
    /// gets no wizard and no hint — nothing at all.
    /// </summary>
    /// <param name="mode">The recorded mode.</param>
    /// <param name="provider">The recorded provider.</param>
    [Theory]
    [InlineData("Offline", ISetupStateService.NoProvider)]
    [InlineData(ISetupStateService.SkippedMode, ISetupStateService.NoProvider)]
    [InlineData(ISetupStateService.SkippedMode, "")]
    public void OfflineOnlyIsASettledOutcomeAndIsNeverNagged(string mode, string provider)
    {
        var state = new SetupCompletionState(true, mode, provider, false);
        Assert.True(state.ChoseEmbeddedOnly);

        var outcome = FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = true,
            WorkspaceCount = 1,
            ProviderConfigured = false,
            ChoseEmbeddedOnly = state.ChoseEmbeddedOnly
        });

        Assert.Equal(FirstRunOutcome.None, outcome);
    }

    /// <summary>
    /// A provider that is missing after setup produces a hint — never a redirect back to the wizard.
    /// </summary>
    [Fact]
    public void HintsRatherThanRedirectsWhenAProviderWentMissingAfterSetup()
    {
        var outcome = FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = true,
            WorkspaceCount = 1,
            ProviderConfigured = false,
            ChoseEmbeddedOnly = false
        });

        Assert.Equal(FirstRunOutcome.ShowProviderHint, outcome);
    }

    /// <summary>Dismissing the hint is permanent: it does not come back on the next launch.</summary>
    [Fact]
    public void DoesNotRepeatTheHintOnceItHasBeenDismissed()
    {
        var outcome = FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = true,
            WorkspaceCount = 1,
            ProviderConfigured = false,
            ChoseEmbeddedOnly = false,
            ProviderHintDismissed = true
        });

        Assert.Equal(FirstRunOutcome.None, outcome);
    }

    /// <summary>A completed install with a working provider sees nothing at all.</summary>
    [Fact]
    public void SaysNothingToACompletedInstallWithAProvider()
    {
        var outcome = FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = true,
            WorkspaceCount = 1,
            ProviderConfigured = true
        });

        Assert.Equal(FirstRunOutcome.None, outcome);
    }

    /// <summary>Every combination of the remaining inputs, with setup recorded as complete.</summary>
    /// <returns>The contexts to assert over.</returns>
    private static IEnumerable<FirstRunContext> EveryCompletedCombination()
    {
        int[] workspaceCounts = [0, 1, 5];
        bool[] flags = [false, true];

        foreach (var workspaceCount in workspaceCounts)
        {
            foreach (var providerConfigured in flags)
            {
                foreach (var embeddedOnly in flags)
                {
                    foreach (var dismissed in flags)
                    {
                        foreach (var onSetupRoute in flags)
                        {
                            yield return new FirstRunContext
                            {
                                SetupComplete = true,
                                WorkspaceCount = workspaceCount,
                                ProviderConfigured = providerConfigured,
                                ChoseEmbeddedOnly = embeddedOnly,
                                ProviderHintDismissed = dismissed,
                                OnSetupRoute = onSetupRoute
                            };
                        }
                    }
                }
            }
        }
    }
}
