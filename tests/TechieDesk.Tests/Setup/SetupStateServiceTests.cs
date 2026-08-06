using TechieDesk.Services.Setup;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Setup;

/// <summary>
/// REQ-FN-050 / REQ-UI-022: what the first-run wizard actually persists, and what the shell reads
/// back from it.
/// </summary>
/// <remarks>
/// The behaviour worth pinning here is that all three exits from the wizard — a full finish, an
/// offline/embedded-only finish and an explicit skip — write the SAME completion flag. That is what
/// makes the once-only guarantee a property of storage rather than of whichever branch the user
/// happened to take.
/// </remarks>
public class SetupStateServiceTests
{
    /// <summary>A brand-new instance reports that it has never been through the wizard.</summary>
    [Fact]
    public async Task ReportsAFreshInstanceAsNeverRun()
    {
        var service = new SetupStateService(new FakeInstanceSettings());

        var state = await service.ReadAsync();

        Assert.False(state.Complete);
        Assert.False(state.ChoseEmbeddedOnly);
        Assert.False(await service.IsFlagCompleteAsync());
    }

    /// <summary>
    /// 🔴 Finishing offline with no provider is a COMPLETED outcome, and it is recorded as a
    /// DELIBERATE one — the distinction acceptance (4) turns on.
    /// </summary>
    [Fact]
    public async Task RecordsAnOfflineFinishAsCompleteAndDeliberate()
    {
        var settings = new FakeInstanceSettings();
        var service = new SetupStateService(settings);

        await service.MarkCompleteAsync("Offline", null, ISetupStateService.NoProvider);
        var state = await service.ReadAsync();

        Assert.True(state.Complete);
        Assert.True(state.ChoseEmbeddedOnly);
        Assert.Equal("Offline", state.Mode);
        Assert.Equal(FirstRunOutcome.None, FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = state.Complete,
            WorkspaceCount = 1,
            ProviderConfigured = false,
            ChoseEmbeddedOnly = state.ChoseEmbeddedOnly
        }));
    }

    /// <summary>
    /// 🔴 "Set up later" settles the question too: it writes the same flag a finish does, so the
    /// wizard cannot return on the next launch.
    /// </summary>
    [Fact]
    public async Task RecordsAnExplicitSkipAsComplete()
    {
        var service = new SetupStateService(new FakeInstanceSettings());

        await service.MarkSkippedAsync();
        var state = await service.ReadAsync();

        Assert.True(state.Complete);
        Assert.True(state.ChoseEmbeddedOnly);
        Assert.Equal(ISetupStateService.SkippedMode, state.Mode);
        Assert.NotEqual(FirstRunOutcome.ShowWizard, FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = state.Complete,
            WorkspaceCount = 1,
            ChoseEmbeddedOnly = state.ChoseEmbeddedOnly
        }));
    }

    /// <summary>A configured provider is recorded, and is not mistaken for a deliberate offline.</summary>
    [Fact]
    public async Task RecordsAChosenProviderWithoutMarkingItOffline()
    {
        var service = new SetupStateService(new FakeInstanceSettings());

        await service.MarkCompleteAsync("Offline", null, "Ollama");
        var state = await service.ReadAsync();

        Assert.True(state.Complete);
        Assert.Equal("Ollama", state.Provider);
        Assert.False(state.ChoseEmbeddedOnly);
    }

    /// <summary>The AppManager base URL still round-trips, and secrets are still not written.</summary>
    [Fact]
    public async Task StoresTheAppManagerBaseUrlAndNothingSecret()
    {
        var settings = new FakeInstanceSettings();
        var service = new SetupStateService(settings);

        await service.MarkCompleteAsync("AppManager", " https://appmanager.example.com ", "Ollama");

        Assert.Equal(
            "https://appmanager.example.com",
            await settings.GetAsync(ISetupStateService.AppManagerBaseUrlKey));
        Assert.DoesNotContain("AppManagerApiKey", settings.Written);
        Assert.DoesNotContain("AppManagerApiSecret", settings.Written);
    }

    /// <summary>The hint dismissal survives a restart, because it is stored rather than held.</summary>
    [Fact]
    public async Task PersistsTheProviderHintDismissal()
    {
        var settings = new FakeInstanceSettings();
        var service = new SetupStateService(settings);
        await service.MarkCompleteAsync("Offline", null, "Ollama");

        await service.DismissProviderHintAsync();

        // A second service over the same store is the restart: nothing is carried in memory.
        var afterRestart = await new SetupStateService(settings).ReadAsync();
        Assert.True(afterRestart.ProviderHintDismissed);
        Assert.Equal(FirstRunOutcome.None, FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = afterRestart.Complete,
            WorkspaceCount = 1,
            ProviderConfigured = false,
            ProviderHintDismissed = afterRestart.ProviderHintDismissed
        }));
    }

    /// <summary>
    /// The wizard's own once-only promise, end to end over the store: complete it once, and the
    /// second launch — reading the same persisted state — is not sent back to it.
    /// </summary>
    [Fact]
    public async Task DoesNotOfferSetupOnTheSecondLaunch()
    {
        var settings = new FakeInstanceSettings();
        await new SetupStateService(settings).MarkCompleteAsync("Offline", null, "Ollama");

        var secondLaunch = await new SetupStateService(settings).ReadAsync();

        Assert.Equal(FirstRunOutcome.None, FirstRunGate.Decide(new FirstRunContext
        {
            SetupComplete = secondLaunch.Complete,
            WorkspaceCount = 1,
            ProviderConfigured = true,
            ChoseEmbeddedOnly = secondLaunch.ChoseEmbeddedOnly
        }));
    }
}
