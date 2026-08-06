using Microsoft.Extensions.Logging;
using TechieDesk.Services.Localization;

namespace TechieDesk.Services;

/// <summary>
/// Holds the live language choice for the window and switches it (REQ-UI-039 / BRD-91).
/// </summary>
/// <remarks>
/// Lives in the head because the switch has to be broadcast to the components the head has already
/// rendered — see <see cref="SetAsync"/>. The testable half (what the offered languages are, where
/// the choice is stored, how a culture is applied) is in TechieDesk.Core.
/// </remarks>
public sealed class LanguageCoordinator
{
    private readonly ILanguageStore store;
    private readonly ILogger<LanguageCoordinator> logger;

    private AppLanguage current = SupportedLanguages.Default;
    private bool loaded;

    /// <summary>Initializes a new instance of the <see cref="LanguageCoordinator"/> class.</summary>
    /// <param name="store">Persistence for the language choice.</param>
    /// <param name="logger">Diagnostics.</param>
    public LanguageCoordinator(ILanguageStore store, ILogger<LanguageCoordinator> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    /// <summary>Gets the language currently in force.</summary>
    public AppLanguage Current => current;

    /// <summary>Gets every language the picker offers.</summary>
    public static IReadOnlyList<AppLanguage> Available => SupportedLanguages.All;

    /// <summary>Loads the stored language and applies it, once per window.</summary>
    /// <returns>A task that completes when the language has been applied.</returns>
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
            AppCulture.Apply(current);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load the language choice; using {Culture}",
                SupportedLanguages.Default.Culture);
            current = SupportedLanguages.Default;
        }
    }

    /// <summary>Persists a language choice and applies it to the process.</summary>
    /// <param name="language">The language to switch to.</param>
    /// <returns>
    /// True when the choice was stored, so the caller can tell the operator it will hold. False when
    /// the write failed — the culture is applied either way, but it will not survive a restart.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The new language appears when the app is next started, not immediately, and that is a real
    /// limitation of a MAUI <c>BlazorWebView</c> rather than a shortcut.</b> Both ways of making an
    /// open window re-render were built and DRIVEN on the shipping Mac Catalyst bundle
    /// (2026-07-28/29, REQ-UI-039), and both failed:
    /// </para>
    /// <para>
    /// 1. <c>NavigateTo(uri, forceLoad: true)</c> — the documented "restart the Blazor app" move.
    /// Reloading the document destroys the JavaScript context the Blazor host is attached to and the
    /// host never re-attaches: the window is left on the raw <c>Loading...</c> host page with
    /// <c>#blazor-error-ui</c> reading "An unhandled error has occurred.", every screen and the whole
    /// sidebar gone, recoverable only by quitting. Measured, not inferred — the unified log records
    /// <c>FrameLoader::loadWithNavigationAction</c> ("Navigation within the same non-HTTP(s)
    /// protocol") followed immediately by two <c>WebPage::runJavaScriptInFrameInScriptWorld: Request
    /// to run JavaScript failed</c> errors. Evidence:
    /// <c>test-results/cluster-a-16-after-german-reload.png</c>.
    /// </para>
    /// <para>
    /// 2. Re-rendering in place — a <c>Changed</c> event the open panels subscribed to, plus a route
    /// round trip that builds brand-new component instances. Neither picks the new culture up. An
    /// <c>IStringLocalizer</c> lookup reads
    /// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> at render time, and a Blazor
    /// render runs under an <see cref="System.Threading.ExecutionContext"/> captured when the
    /// renderer started, so it keeps resolving the culture that was in force at startup no matter
    /// what this method sets afterwards. Proven live: after choosing English on a German window,
    /// navigating to Event Log and back to the Branding tab still rendered German
    /// (<c>test-results/cluster-a-35-after-nav-roundtrip.png</c>) — and the same build, restarted,
    /// came up entirely in the chosen language.
    /// </para>
    /// <para>
    /// So the honest implementation is: store it, apply it to the process so the next launch is
    /// right, and let the picker SAY that a restart is needed. A silent choice that appears not to
    /// work is worse than a stated one that does.
    /// </para>
    /// </remarks>
    public async Task<bool> SetAsync(AppLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);

        if (string.Equals(language.Culture, current.Culture, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        current = language;
        loaded = true;
        AppCulture.Apply(language);

        try
        {
            await store.SaveAsync(language).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            // Applied to the process but not stored, so the window is already using it and the next
            // launch will not. Worth a log line and an honest return value, not a refusal.
            logger.LogError(ex, "Could not persist the language choice");
            return false;
        }
    }
}
