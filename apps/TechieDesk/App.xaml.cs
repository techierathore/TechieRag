using Microsoft.Extensions.Localization;
using TechieDesk.Resources;
using TechieDesk.Services.Install;

namespace TechieDesk;

/// <summary>
/// The MAUI application shell (REQ-FN-035).
/// </summary>
public partial class App : Application
{
    private readonly SingleInstanceState singleInstance;
    private readonly IStringLocalizer<AppStrings> localizer;

    /// <summary>Narrowest the window may be dragged, in desktop points (REQ-UI-041).</summary>
    /// <remarks>
    /// <para>
    /// BRD-133 fixes this at 1024 x 720. It is a floor, not a default: the sidebar shell plus a
    /// document table stops being usable below it, and a desktop window — unlike a browser tab —
    /// can be dragged to any size the OS allows unless the app says otherwise.
    /// </para>
    /// <para>
    /// Set on the window below on every head. On Mac Catalyst it must be pre-scaled by
    /// <see cref="CatalystMinimumSizeCorrection"/> — see there.
    /// </para>
    /// </remarks>
    public const double MinimumWindowWidth = 1024;

    /// <summary>Shortest the window may be dragged, in desktop points (REQ-UI-041).</summary>
    public const double MinimumWindowHeight = 720;

#if MACCATALYST
    /// <summary>
    /// Scales the BRD-133 floor from macOS points into what MAUI's Catalyst window handler wants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Catalyst draws the iPad interface at 77% of its natural size, and MAUI passes
    /// <c>MinimumWidth</c>/<c>MinimumHeight</c> straight through without undoing that — so an
    /// uncorrected 1024 x 720 lands as a 789 x 555 floor on screen. Measured on the running app
    /// 2026-07-28: 789/1024 = 0.7705 and 555/720 = 0.7708, i.e. exactly the idiom scale on both
    /// axes. Dividing by it lands the floor on 1024 x 720 to the point — re-measured after the fix.
    /// </para>
    /// <para>
    /// This replaces an earlier attempt that set <c>UIWindowScene.SizeRestrictions.MinimumSize</c>
    /// from <c>MainPage</c>. That code never worked: it ran from <c>OnHandlerChanged</c>, before the
    /// window scene existed, found nothing to set and returned silently, which is why the shipped
    /// floor was the OS default of 515 x 319. Fixing its timing did not help either — MAUI applies
    /// its own value afterwards and wins, so the scene restriction was dead weight whatever it was
    /// set to (proved by A/B: removing it entirely leaves the floor at 1024 x 720). One knob, here.
    /// </para>
    /// </remarks>
    private const double CatalystMinimumSizeCorrection = 1.0 / 0.77;
#else
    /// <summary>No correction is needed off Mac Catalyst; the Windows head is 1:1.</summary>
    private const double CatalystMinimumSizeCorrection = 1.0;
#endif

    /// <summary>Width the window opens at on a first run, in desktop points.</summary>
    /// <remarks>
    /// Honoured by the Windows head. Mac Catalyst restores the window frame the user last left and
    /// ignores a requested size, which is the platform's own convention and is left alone.
    /// </remarks>
    public const double DefaultWindowWidth = 1440;

    /// <summary>Height the window opens at on a first run, in desktop points.</summary>
    public const double DefaultWindowHeight = 900;

    /// <summary>Creates the application.</summary>
    /// <param name="singleInstance">
    /// The launch-time single-instance decision (REQ-FN-051 clause 3), registered as a singleton by
    /// <c>MauiProgram</c>. Injected rather than read from a static so the guard's one consumer is
    /// visible in the type's signature.
    /// </param>
    /// <param name="localizer">
    /// Resolves the second-instance refusal's resource keys (REQ-UI-055). Resolvable in this path:
    /// <c>MauiProgram</c> registers localization and builds the container before its
    /// <c>IsPrimaryInstance</c> early return — see <see cref="SecondInstancePage"/>.
    /// </param>
    public App(SingleInstanceState singleInstance, IStringLocalizer<AppStrings> localizer)
    {
        this.singleInstance = singleInstance;
        this.localizer = localizer;
        InitializeComponent();
    }

    /// <summary>Creates the main window hosting the Blazor UI.</summary>
    /// <param name="activationState">Platform activation state.</param>
    /// <returns>The application window.</returns>
    /// <remarks>
    /// REQ-UI-041 (BRD-133): the window carries the 1024 x 720 minimum. The native menu bar lives on
    /// <see cref="MainPage"/> because MAUI sources a window's menu from its page, and the OS file
    /// pickers are called from the Razor components that ingest — see <c>MainPage.xaml</c> and
    /// <c>Components/Pages/DataStorage.razor</c>.
    /// </remarks>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        // REQ-FN-051 clause 3: a second copy pointed at a data directory another live copy already
        // holds gets a window that SAYS SO, not a silent exit. It never reaches MainPage, so it never
        // opens the database the other copy is writing.
        if (!singleInstance.IsPrimaryInstance)
        {
            return new Window(new SecondInstancePage(singleInstance, localizer))
            {
                Title = localizer[SingleInstanceState.RefusalTitleKey].Value
            };
        }

        return new Window(new MainPage())
        {
            Title = "TechieDesk",
            MinimumWidth = MinimumWindowWidth * CatalystMinimumSizeCorrection,
            MinimumHeight = MinimumWindowHeight * CatalystMinimumSizeCorrection,
            Width = DefaultWindowWidth,
            Height = DefaultWindowHeight
        };
    }
}
