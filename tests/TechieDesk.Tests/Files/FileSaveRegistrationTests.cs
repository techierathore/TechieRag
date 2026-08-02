using Microsoft.Extensions.DependencyInjection;
using TechieDesk.Services.Files;
using TechieDesk.Services.Threads;
using Xunit;

namespace TechieDesk.Tests.Files;

/// <summary>
/// REQ-FN-010 (BRD-35): the export service graph the head builds — a platform save panel, the
/// TryAdd fallback behind it, and the export service on top — resolves and honours its ordering.
/// </summary>
public sealed class FileSaveRegistrationTests
{
    /// <summary>
    /// The registrations MauiProgram makes are sufficient to construct ThreadExportService, so the
    /// component's @inject cannot fail at runtime with a missing dependency.
    /// </summary>
    [Fact]
    public void ExportServiceResolvesFromTheHeadRegistrations()
    {
        var services = new ServiceCollection();
        services.AddTechieDeskFileSave();
        services.AddSingleton<ThreadExporter>();
        services.AddSingleton<ThreadExportService>();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ThreadExportService>());
    }

    /// <summary>
    /// A platform save panel registered before AddTechieDeskFileSave survives its TryAdd — the
    /// ordering the Mac Catalyst head depends on, since the fallback would otherwise win and no
    /// export could ever write a file.
    /// </summary>
    [Fact]
    public void PlatformSaveServiceRegisteredFirstWinsOverTheFallback()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSaveService, SupportedFileSaveService>();
        services.AddTechieDeskFileSave();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<SupportedFileSaveService>(provider.GetRequiredService<IFileSaveService>());
    }

    /// <summary>
    /// With no platform registration the honest fallback resolves, so a head without a save panel
    /// reports a failure instead of silently writing nothing.
    /// </summary>
    [Fact]
    public void FallbackResolvesWhenNoPlatformSaveServiceIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddTechieDeskFileSave();

        using var provider = services.BuildServiceProvider();

        var saveService = provider.GetRequiredService<IFileSaveService>();
        Assert.IsType<UnsupportedFileSaveService>(saveService);
        Assert.False(saveService.IsSupported);
    }

    /// <summary>A stand-in for a head's native save panel registration.</summary>
    private sealed class SupportedFileSaveService : IFileSaveService
    {
        public bool IsSupported => true;

        public Task<FileSaveResult> SaveTextAsync(
            string suggestedFileName,
            string contentType,
            string content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FileSaveResult.Cancelled());
    }
}
