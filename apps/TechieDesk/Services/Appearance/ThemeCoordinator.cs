using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TechieDesk.Services.Appearance;

namespace TechieDesk.Services;

/// <summary>
/// Holds the live theme state for the window and pushes it into the document (REQ-UI-038 / BRD-90).
/// </summary>
/// <remarks>
/// <para>
/// Lives in the head rather than in TechieDesk.Core because it is the half of the feature that is
/// unavoidably platform-bound: it drives <c>&lt;html&gt;</c>, which sits outside Blazor's render
/// tree, so it can only be reached through JS interop. The half that is testable — what a theme mode
/// and an accent ARE, and where the choice is stored — is <c>AppearanceStore</c> in Core, and that
/// is what the unit tests cover.
/// </para>
/// <para>
/// Scoped, which for a BlazorWebView means one instance for the lifetime of the window. That is what
/// lets the toggle in the topbar and the accent swatches on the settings screen stay in step: both
/// read <see cref="Current"/> and both subscribe to <see cref="Changed"/>, so neither has to know
/// the other exists.
/// </para>
/// </remarks>
public sealed class ThemeCoordinator : IAsyncDisposable
{
    private readonly IJSRuntime jsRuntime;
    private readonly IAppearanceStore store;
    private readonly ILogger<ThemeCoordinator> logger;

    private IJSObjectReference? module;
    private AppearanceSettings current = AppearanceSettings.Defaults;
    private bool loaded;

    /// <summary>Initializes a new instance of the <see cref="ThemeCoordinator"/> class.</summary>
    /// <param name="jsRuntime">The WebView JS runtime.</param>
    /// <param name="store">Persistence for the appearance choices.</param>
    /// <param name="logger">Diagnostics.</param>
    public ThemeCoordinator(
        IJSRuntime jsRuntime, IAppearanceStore store, ILogger<ThemeCoordinator> logger)
    {
        this.jsRuntime = jsRuntime;
        this.store = store;
        this.logger = logger;
    }

    /// <summary>Gets the appearance settings currently applied to the window.</summary>
    public AppearanceSettings Current => current;

    /// <summary>Gets the accent currently applied.</summary>
    public AccentColor Accent => current.Accent;

    /// <summary>Raised after the applied settings change, so open surfaces can re-render.</summary>
    public event Action? Changed;

    /// <summary>
    /// Loads the stored choice and applies it, once per window. Safe to call from every surface's
    /// first render.
    /// </summary>
    /// <returns>A task that completes when the theme has been applied.</returns>
    /// <remarks>
    /// Idempotent by design. The initializer in <c>Routes.razor</c> is what normally runs this, but
    /// making it safe to call again means a surface that renders before the initializer does — or
    /// after a hot reload — does not have to reason about ordering.
    /// </remarks>
    public async Task EnsureAppliedAsync()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;

        try
        {
            current = await store.LoadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A settings read must never leave the app unpainted. The defaults are a working theme.
            logger.LogWarning(ex, "Could not load the appearance settings; using the defaults");
            current = AppearanceSettings.Defaults;
        }

        await ApplyAsync().ConfigureAwait(false);
        Changed?.Invoke();
    }

    /// <summary>Applies and persists a theme mode.</summary>
    /// <param name="mode">The mode to apply.</param>
    /// <returns>A task that completes when the mode has been applied and stored.</returns>
    public Task SetModeAsync(ThemeMode mode) => UpdateAsync(current with { Mode = mode });

    /// <summary>Applies and persists an accent colour.</summary>
    /// <param name="accentKey">The accent key; see <see cref="AccentPalette"/>.</param>
    /// <returns>A task that completes when the accent has been applied and stored.</returns>
    public Task SetAccentAsync(string accentKey) =>
        UpdateAsync(current with { AccentKey = AccentPalette.Resolve(accentKey).Key });

    /// <summary>
    /// Reports whether the palette in force right now is the dark one, resolving
    /// <see cref="ThemeMode.System"/> against the operating system.
    /// </summary>
    /// <returns>True when the dark palette is being rendered.</returns>
    public async Task<bool> IsDarkAsync()
    {
        if (current.Mode != ThemeMode.System)
        {
            return current.Mode == ThemeMode.Dark;
        }

        try
        {
            var loadedModule = await LoadModuleAsync().ConfigureAwait(false);
            return await loadedModule.InvokeAsync<bool>("systemPrefersDark").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read the system colour-scheme preference");
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // The window closed before the module reference was released. Nothing to clean up.
        }
    }

    private async Task UpdateAsync(AppearanceSettings next)
    {
        current = next;
        loaded = true;

        await ApplyAsync().ConfigureAwait(false);
        Changed?.Invoke();

        try
        {
            await store.SaveAsync(next).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The theme is already on screen. A failed write means it will not survive a restart,
            // which is worth a log line but not worth undoing what the user just asked for.
            logger.LogError(ex, "Could not persist the appearance settings");
        }
    }

    private async Task ApplyAsync()
    {
        try
        {
            var loadedModule = await LoadModuleAsync().ConfigureAwait(false);
            var accent = current.Accent;

            // BOTH palettes are handed over, not the resolved one. See paintAccent() in theme.js:
            // resolving here would strand the accent on one variant while the OS moves the window to
            // the other under a "Match system" choice.
            await loadedModule.InvokeVoidAsync(
                "apply",
                ModeName(current.Mode),
                accent.PrimaryFor(false),
                accent.PrimaryForegroundFor(false),
                accent.PrimaryFor(true),
                accent.PrimaryForegroundFor(true)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply the theme to the document");
        }
    }

    private async Task<IJSObjectReference> LoadModuleAsync() =>
        module ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./js/theme.js").ConfigureAwait(false);

    // The JS module speaks the CSS vocabulary ('light'/'dark'/'system'), not the enum's.
    private static string ModeName(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "light",
        ThemeMode.Dark => "dark",
        _ => "system"
    };
}
