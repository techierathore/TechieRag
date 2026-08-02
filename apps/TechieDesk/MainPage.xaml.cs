using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using TechieDesk.Services.Storage;
using TechieDeskDb;
// REQ-UI-052: aliased rather than `using TechieDesk.Resources`, for the same reason _Imports.razor
// aliases it — MAUI treats a `Resources` folder as its own asset root and an unqualified
// `Resources` inside a MAUI head is ambiguous enough to be worth not writing.
using AppStrings = TechieDesk.Resources.AppStrings;

namespace TechieDesk;

/// <summary>
/// The single page hosting the <c>BlazorWebView</c> (REQ-FN-035), and the owner of the native menu
/// bar (REQ-UI-041).
/// </summary>
/// <remarks>
/// MAUI sources a window's menu bar from its page, not from the <see cref="Application"/>, so the
/// menu is built here. Every item does something real: it either navigates the hosted Blazor router,
/// opens an OS picker, or reveals a folder in the file manager. Menu entries that would need a
/// dialog owned by a Razor component are deliberately absent rather than present and inert.
/// </remarks>
public partial class MainPage : ContentPage
{
    /// <summary>Route of the data and storage settings surface (REQ-UI-041).</summary>
    public const string DataStorageRoute = "/settings/data";

    /// <summary>Route of the file-ingestion screen the OS pickers feed.</summary>
    public const string IngestionRoute = "/ingestion";

    /// <summary>Route of the update surface (REQ-FN-038b).</summary>
    public const string UpdatesRoute = "/settings/updates";

    /// <summary>Route of the TechieRag configuration screen (REQ-UI-049).</summary>
    /// <remarks>
    /// Was <c>/settings</c> until 2026-07-28. Renamed because three different screens were called
    /// some form of "settings" and this was not the one a user means by the word; see the header of
    /// <c>Components/Pages/RagConfig.razor</c>.
    /// </remarks>
    public const string RagConfigRoute = "/rag-config";

    /// <summary>
    /// The UI strings for the menu bar and the dialogs its items raise (REQ-UI-052).
    /// </summary>
    /// <remarks>
    /// Nullable and resolved rather than injected because MAUI constructs this page with
    /// <c>new MainPage()</c> from <c>App.CreateWindow</c>, so there is no constructor injection to
    /// use. A null localizer is not a reason to refuse to open a window, so <see cref="Text"/>
    /// degrades to the key instead of throwing — visibly wrong, but the app still runs, and the
    /// menu-bar guard in the test suite is what stops it ever being wrong quietly.
    /// </remarks>
    private readonly IStringLocalizer<AppStrings>? strings;

    /// <summary>Creates the page and installs the native menu bar.</summary>
    /// <remarks>
    /// REQ-UI-052: the menu is built in C#, so it needs the localizer the razor components get by
    /// injection. This runs after the MAUI host is built AND after
    /// <c>MauiProgram.ApplyStoredLanguage</c> has set <c>CultureInfo.CurrentUICulture</c>, so the
    /// menu resolves in the user's language the first time it is drawn — which matters because
    /// UIKit builds the menu bar once and every lookup below therefore happens exactly once.
    /// </remarks>
    public MainPage()
    {
        strings = IPlatformApplication.Current?.Services.GetService<IStringLocalizer<AppStrings>>();
        InitializeComponent();
        BuildMenuBar();
    }

    /// <summary>Resolves one UI string for the menu bar.</summary>
    /// <param name="key">The <c>AppStrings</c> resource key.</param>
    /// <returns>The localized text, or the key itself if the localizer could not be resolved.</returns>
    private string Text(string key) => strings is null ? key : strings[key].Value;

    /// <summary>Resolves one composite-format UI string for the menu bar.</summary>
    /// <param name="key">The <c>AppStrings</c> resource key.</param>
    /// <param name="arguments">The values its placeholders name.</param>
    /// <returns>The formatted localized text, or the key itself if the localizer is unavailable.</returns>
    private string Text(string key, params object[] arguments) =>
        strings is null ? key : strings[key, arguments].Value;

    /// <summary>Asks the platform to re-read the menu bar once this page has a handler.</summary>
    /// <remarks>
    /// REQ-UI-041. On Mac Catalyst the menu bar is assembled by UIKit's
    /// <c>UIApplicationDelegate.BuildMenu</c>, which the system calls ONCE during launch — before
    /// this page exists. Menu items declared in the constructor therefore miss that pass entirely,
    /// and the app shows only the stock TechieDesk/File/Edit/Format/View/Window/Help set; measured,
    /// not assumed. Marking the main menu system as needing a rebuild makes UIKit call
    /// <c>BuildMenu</c> again now that <see cref="Page.MenuBarItems"/> is populated. No-ops on any
    /// platform whose menu bar is not built this way.
    /// </remarks>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if MACCATALYST || IOS
        UIKit.UIMenuSystem.MainSystem?.SetNeedsRebuild();
#endif
#if DEBUG
        // REQ-NFR-005: the axe-core sweep, armed only by TECHIEDESK_A11Y_SCAN=1. Inert otherwise,
        // and absent from Release entirely. See A11yScanRunner for why the scan has to run in-process.
        _ = A11yScanRunner.MaybeRunAsync(blazorWebView);
#endif
    }


    /// <summary>Builds the native menu bar and its standard keyboard shortcuts (REQ-UI-041).</summary>
    /// <remarks>
    /// <para>
    /// The platform contributes the application menu itself (About, Services, Hide, Quit ⌘Q on
    /// macOS) and the window commands; these are the app's own menus, appended after it.
    /// </para>
    /// <para>
    /// REQ-UI-052 (BRD-91): every caption here is a RESOURCE KEY, not English text. The menu bar is
    /// the one user-visible surface built in C# rather than in markup, so the razor coverage counter
    /// cannot see it and it stayed English through three localization tranches without anything
    /// going red. What guards it now is <c>MenuBarLocalizationTests</c>, which reads THIS FILE and
    /// requires every caption to be a <c>Menu*</c> key that resolves in every shipped language —
    /// so an English literal added here fails on the change that adds it, exactly as it would in a
    /// registered .razor file. The accelerators are asserted there too: a caption is translated,
    /// a key equivalent never is.
    /// </para>
    /// </remarks>
    private void BuildMenuBar()
    {
        var file = new MenuBarItem { Text = Text("MenuFile") };
        file.Add(MenuItem("MenuFileImportDocuments", "O", PickFilesAsync));
        file.Add(MenuItem("MenuFileImportFolder", "O", PickFolderAsync, shift: true));
        file.Add(new MenuFlyoutSeparator());
        file.Add(MenuItem("MenuFileRevealDataFolder", "R", () => RevealAsync(DataDirectoryPath()), shift: true));
        file.Add(MenuItem("MenuFileRevealLogsFolder", "L", () => RevealAsync(LogDirectoryPath()), shift: true));

        var view = new MenuBarItem { Text = Text("MenuGo") };
        view.Add(MenuItem("MenuGoHome", "1", () => NavigateAsync("/")));
        view.Add(MenuItem("MenuGoChat", "2", () => NavigateAsync("/chat")));
        view.Add(MenuItem("MenuGoIngestion", "3", () => NavigateAsync(IngestionRoute)));
        view.Add(MenuItem("MenuGoTokenUsage", "4", () => NavigateAsync("/token-usage")));
        view.Add(new MenuFlyoutSeparator());
        // REQ-UI-049 (2026-07-28): ⌘, is macOS's standard Settings shortcut, so it must open the
        // screen a user means by "settings" — App Settings. It previously opened the TechieRag
        // configuration screen, which is a different thing and is now reachable under its own name.
        view.Add(MenuItem("MenuGoSettings", ",", () => NavigateAsync("/admin/settings")));
        view.Add(MenuItem("MenuGoRagConfiguration", null, () => NavigateAsync(RagConfigRoute)));
        view.Add(MenuItem("MenuGoLlmSettings", "L", () => NavigateAsync("/llm-settings")));
        view.Add(MenuItem("MenuGoDataStorage", "D", () => NavigateAsync(DataStorageRoute)));

        // REQ-UI-050 — interface scale. The keyboard handler in wwwroot/js/zoom.js already answers
        // Cmd +/-/0 whenever the web view has focus; these exist because a shortcut nobody can find
        // is not a feature, and because the menu keeps working when focus is outside the web view.
        var appearance = new MenuBarItem { Text = Text("MenuView") };
        appearance.Add(MenuItem("MenuViewZoomIn", "+", () => ZoomAsync("zoomIn")));
        appearance.Add(MenuItem("MenuViewZoomOut", "-", () => ZoomAsync("zoomOut")));
        appearance.Add(MenuItem("MenuViewActualSize", "0", () => ZoomAsync("reset")));

        var help = new MenuBarItem { Text = Text("MenuHelp") };
        // REQ-FN-038b. On macOS "Check for Updates…" conventionally sits in the application menu,
        // but that menu is owned by the platform and BuildMenu can only append siblings, so Help is
        // where it can actually be placed rather than where it would ideally live.
        help.Add(MenuItem("MenuHelpCheckForUpdates", key: null, () => NavigateAsync(UpdatesRoute)));
        help.Add(MenuItem("MenuHelpVersionAndDataFolder", key: null, ShowAboutAsync));
        help.Add(MenuItem("MenuHelpWhereIsMyData", key: null, () => NavigateAsync(DataStorageRoute)));

        MenuBarItems.Add(file);
        MenuBarItems.Add(view);
        MenuBarItems.Add(appearance);
        MenuBarItems.Add(help);
    }

    /// <summary>Builds one menu item with an optional standard shortcut.</summary>
    /// <param name="textKey">The <c>AppStrings</c> key of the menu item caption (REQ-UI-052).</param>
    /// <param name="key">The accelerator key, or null for no shortcut.</param>
    /// <param name="handler">What the item does when chosen.</param>
    /// <param name="shift">True to add Shift to the primary modifier.</param>
    /// <returns>The configured menu item.</returns>
    /// <remarks>
    /// <para>
    /// The primary modifier is Command on macOS/Mac Catalyst and Control on Windows — the platform's
    /// own convention. Hard-coding either one would produce shortcuts that read as foreign on the
    /// other head, which is the whole point of a NATIVE menu bar.
    /// </para>
    /// <para>
    /// REQ-UI-052: <paramref name="textKey"/> is looked up, <paramref name="key"/> is NOT. A key
    /// equivalent is part of the platform contract a user has memorised — ⌘, opens settings on a
    /// Hindi Mac exactly as it does on an English one — so translating the caption must never move
    /// the shortcut. Keeping them in separate parameters is what makes that hard to get wrong, and
    /// <c>MenuBarLocalizationTests</c> asserts the whole table.
    /// </para>
    /// </remarks>
    private MenuFlyoutItem MenuItem(string textKey, string? key, Func<Task> handler, bool shift = false)
    {
        var item = new MenuFlyoutItem { Text = Text(textKey) };
        item.Clicked += (_, _) => _ = handler();

        if (string.IsNullOrEmpty(key))
        {
            return item;
        }

        var modifiers = OperatingSystem.IsWindows()
            ? KeyboardAcceleratorModifiers.Ctrl
            : KeyboardAcceleratorModifiers.Cmd;
        if (shift)
        {
            modifiers |= KeyboardAcceleratorModifiers.Shift;
        }

        item.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = modifiers, Key = key });
        return item;
    }

    /// <summary>
    /// Changes the interface scale by invoking the web view's zoom module (REQ-UI-050).
    /// </summary>
    /// <param name="function">The exported function to call: <c>zoomIn</c>, <c>zoomOut</c> or <c>reset</c>.</param>
    /// <returns>A task that completes once the script has been dispatched.</returns>
    /// <remarks>
    /// The scale lives in CSS and localStorage rather than in .NET, so this deliberately reaches
    /// into the page instead of round-tripping through a Blazor component: there is no application
    /// state to keep in step, and a menu item must keep working on every route including the auth
    /// screens, which use a different layout.
    /// </remarks>
    private async Task ZoomAsync(string function)
    {
        try
        {
            // Same bridge NavigateAsync uses, for the same reason: IJSRuntime is scoped to the web
            // view's own service scope and only exists on the component dispatcher.
            await blazorWebView.TryDispatchAsync(async services =>
            {
                var js = services.GetRequiredService<IJSRuntime>();
                await using var module = await js.InvokeAsync<IJSObjectReference>(
                    "import", "./js/zoom.js");
                await module.InvokeVoidAsync(function);
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or ObjectDisposedException
                                              or JSException
                                              or JSDisconnectedException)
        {
            // A menu click must never take the window down, and the keyboard handler in zoom.js
            // still works regardless — so this degrades rather than reports.
            await ReportAsync(
                Text("MenuAlertInterfaceScaleTitle"),
                Text("MenuAlertInterfaceScaleFailed", exception.Message));
        }
    }

    /// <summary>
    /// Navigates the hosted Blazor router from native code.
    /// </summary>
    /// <param name="route">An app-relative route such as <c>/settings/data</c>.</param>
    /// <returns>A task that completes once the navigation has been dispatched.</returns>
    /// <remarks>
    /// <c>TryDispatchAsync</c> is the supported bridge: it runs the callback on the component
    /// dispatcher with the web view's own service scope, which is the only place the scoped
    /// <see cref="NavigationManager"/> exists. Returns without effect before the web view has
    /// started, rather than throwing into a menu click.
    /// </remarks>
    private async Task NavigateAsync(string route)
    {
        try
        {
            await blazorWebView.TryDispatchAsync(services =>
                services.GetRequiredService<NavigationManager>().NavigateTo(route));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            await ReportAsync(
                Text("MenuAlertNavigationTitle"),
                Text("MenuAlertNavigationFailed", route, exception.Message));
        }
    }

    /// <summary>Opens the OS file picker and queues the chosen documents for ingestion.</summary>
    /// <returns>A task that completes once the selection has been queued.</returns>
    private async Task PickFilesAsync()
    {
        try
        {
            // No FileTypes filter: the accept/reject matrix is UploadTypePolicy's, and a picker that
            // hides a supported type is worse than one that lets it through to a clear rejection.
            var picked = await FilePicker.Default.PickMultipleAsync(
                new PickOptions { PickerTitle = Text("MenuPickDocumentsTitle") });
            if (picked is null)
            {
                return;
            }

            var paths = picked.Select(result => result.FullPath).Where(File.Exists).ToArray();
            if (paths.Length == 0)
            {
                return;
            }

            DesktopImportQueue.QueueFiles(paths);
            await NavigateAsync(IngestionRoute);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReportAsync(
                Text("MenuAlertFilePickerTitle"),
                Text("MenuAlertFilePickerFailed", exception.Message));
        }
    }

    /// <summary>Opens the OS folder picker and queues the chosen folder for ingestion.</summary>
    /// <returns>A task that completes once the selection has been queued.</returns>
    private async Task PickFolderAsync()
    {
        if (!DesktopFolderPicker.IsSupported)
        {
            await ReportAsync(Text("MenuAlertFolderPickerTitle"), Text("MenuAlertNoFolderPicker"));
            return;
        }

        try
        {
            // A cancelled pick returns null and is not an error — it must not raise a dialog.
            var folder = await DesktopFolderPicker.PickAsync();
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            DesktopImportQueue.QueueFolder(folder);
            await NavigateAsync(IngestionRoute);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReportAsync(
                Text("MenuAlertFolderPickerTitle"),
                Text("MenuAlertFolderPickerFailed", exception.Message));
        }
    }

    /// <summary>Reveals a folder in the host's file manager, reporting failure honestly.</summary>
    /// <param name="path">Absolute path to reveal.</param>
    /// <returns>A task that completes once the reveal has been attempted.</returns>
    private async Task RevealAsync(string path)
    {
        var outcome = FileManagerReveal.Reveal(path);
        if (!outcome.Launched)
        {
            // REQ-UI-055: the outcome carries a resource key and its values, not a sentence.
            await ReportAsync(
                Text("MenuAlertRevealTitle"), Text(outcome.MessageKey, outcome.Arguments));
        }
    }

    /// <summary>Shows what the install is and where its state lives.</summary>
    /// <returns>A task that completes when the dialog is dismissed.</returns>
    /// <remarks>
    /// REQ-UI-052: the title is the platform's own application name rather than a resource key. It
    /// is the product name, which is not translated in either language and — on a white-labelled
    /// install (REQ-UI-037) — is not "TechieDesk" at all, so a resource string would be wrong twice.
    /// </remarks>
    private Task ShowAboutAsync() => ReportAsync(
        AppInfo.Current.Name,
        Text("MenuAboutVersion", AppInfo.Current.VersionString, AppInfo.Current.BuildString)
        + Environment.NewLine + Environment.NewLine
        + Text("MenuAboutDataDirectory") + Environment.NewLine + DataDirectoryPath());

    /// <summary>Shows a native message, on the UI thread whichever thread called.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Dialog body.</param>
    /// <returns>A task that completes when the dialog is dismissed.</returns>
    private Task ReportAsync(string title, string message) =>
        Dispatcher.DispatchAsync(() => DisplayAlert(title, message, Text("MenuAlertDismiss")));

    /// <summary>Gets the per-user data directory (REQ-FN-037).</summary>
    /// <returns>An absolute path.</returns>
    private static string DataDirectoryPath() =>
        DataDirectory.Resolve(Environment.GetEnvironmentVariable("AppDb__DataDirectory"));

    /// <summary>Gets the rolling log directory inside the data directory (REQ-NFR-009).</summary>
    /// <returns>An absolute path.</returns>
    private static string LogDirectoryPath() =>
        Path.Combine(DataDirectoryPath(), DataDirectory.LogDirectoryName);
}
