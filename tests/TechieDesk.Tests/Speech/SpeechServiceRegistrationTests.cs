using Microsoft.Extensions.DependencyInjection;
using TechieDesk.Services.Speech;
using Xunit;

namespace TechieDesk.Tests.Speech;

/// <summary>
/// Unit tests for the speech service registration (REQ-UI-035 / REQ-UI-036).
/// </summary>
public class SpeechServiceRegistrationTests
{
    /// <summary>Verifies a host with no platform gets the unavailable fallbacks, not a missing service.</summary>
    [Fact]
    public void FallbacksAreRegisteredForAHostWithNoPlatform()
    {
        var provider = new ServiceCollection().AddTechieDeskSpeech().BuildServiceProvider();

        Assert.IsType<UnsupportedDictationService>(provider.GetRequiredService<IDictationService>());
        Assert.IsType<UnsupportedReadAloudService>(provider.GetRequiredService<IReadAloudService>());
    }

    /// <summary>
    /// Verifies a platform implementation registered first survives, which is the whole point of
    /// TryAdd here: a fallback that silently won would disable dictation on a machine that supports
    /// it.
    /// </summary>
    [Fact]
    public void PlatformRegistrationWins()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDictationService, StubPlatformDictationService>();

        var provider = services.AddTechieDeskSpeech().BuildServiceProvider();

        Assert.IsType<StubPlatformDictationService>(provider.GetRequiredService<IDictationService>());
    }

    /// <summary>Verifies the fallback reports itself unavailable rather than throwing.</summary>
    [Fact]
    public async Task FallbackDictationReportsUnsupportedWithoutThrowing()
    {
        var service = new UnsupportedDictationService();

        Assert.False(service.IsSupported);
        // REQ-UI-055: the fallback carries no reason of its own; the mic button renders
        // UnsupportedDictationService.ReasonKey in the reader's language instead.
        Assert.Null(service.UnsupportedReason);
        Assert.Equal(DictationPermission.Unsupported, await service.RequestPermissionAsync());
        await service.StartAsync(new DictationCallbacks());
        await service.StopAsync();
    }

    /// <summary>Verifies the read-aloud fallback reports itself unavailable rather than throwing.</summary>
    [Fact]
    public async Task FallbackReadAloudReportsUnsupportedWithoutThrowing()
    {
        var service = new UnsupportedReadAloudService();

        Assert.False(service.IsSupported);
        Assert.False(service.IsSpeaking);

        // REQ-UI-055: a host with no synthesiser can speak NO language, so this is false for the
        // default culture too — a caller must not be told "the platform can say this".
        Assert.False(await service.CanSpeakAsync("en"));
        Assert.False(await service.CanSpeakAsync("hi"));

        await service.SpeakAsync("anything");
        await service.SpeakAsync("anything", "hi");
        await service.StopAsync();
    }
}

/// <summary>
/// Stands in for a head's platform dictation service in registration tests.
/// </summary>
internal sealed class StubPlatformDictationService : IDictationService
{
    /// <inheritdoc/>
    public bool IsSupported => true;

    /// <inheritdoc/>
    public string? UnsupportedReason => null;

    /// <inheritdoc/>
    public Task<DictationPermission> RequestPermissionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DictationPermission.Granted);

    /// <inheritdoc/>
    public Task StartAsync(DictationCallbacks callbacks, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
