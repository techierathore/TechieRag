using Microsoft.Extensions.Localization;
using TechieDesk.Resources;
using TechieDesk.Services.Install;

namespace TechieDesk;

/// <summary>
/// The window a SECOND copy of TechieDesk shows instead of the app (REQ-FN-051 clause 3).
/// </summary>
/// <remarks>
/// <para>
/// The acceptance clause asks the second instance to refuse. It does not say "exit", and exiting is
/// the wrong answer: a process that vanishes on launch is indistinguishable from a crash, so the
/// user's next move is to double-click again, and the product looks broken rather than careful. This
/// page is the refusal made visible — it names the data folder, names the process that holds it, and
/// says how the situation clears.
/// </para>
/// <para>
/// Built in code rather than XAML, and — since REQ-UI-055 — through <c>IStringLocalizer</c> rather
/// than in English. The earlier note here said a refused instance had "no localized culture in place
/// to read from"; that was half right. <c>MauiProgram</c> registers localization and builds the
/// container BEFORE its <c>IsPrimaryInstance</c> early return, so the localizer resolves fine. What
/// the refused instance still must not do is read the STORED language, because that lives in the
/// database the live copy is writing. So this resolves against
/// <c>CultureInfo.CurrentUICulture</c> — the operating system's language — and opens no file at all.
/// A Hindi machine gets a Hindi refusal; an install whose in-app language differs from its OS
/// language gets the OS language, which is still a language the user reads.
/// </para>
/// </remarks>
public sealed class SecondInstancePage : ContentPage
{
    /// <summary>Initializes a new instance of the <see cref="SecondInstancePage"/> class.</summary>
    /// <param name="state">The launch-time single-instance decision being reported.</param>
    /// <param name="localizer">Resolves the refusal's resource keys (REQ-UI-055).</param>
    /// <exception cref="ArgumentNullException">Either argument was null.</exception>
    public SecondInstancePage(SingleInstanceState state, IStringLocalizer<AppStrings> localizer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(localizer);

        var title = localizer[SingleInstanceState.RefusalTitleKey].Value;

        Title = title;
        Padding = new Thickness(48);

        var heading = new Label
        {
            Text = title,
            FontSize = 24,
            FontAttributes = FontAttributes.Bold
        };

        var detail = new Label
        {
            Text = localizer[state.RefusalDetailKey, [.. state.RefusalDetailArguments]].Value,
            FontSize = 15,
            LineBreakMode = LineBreakMode.WordWrap
        };

        var quit = new Button
        {
            Text = localizer[SingleInstanceState.RefusalCloseButtonKey].Value,
            HorizontalOptions = LayoutOptions.Start
        };
        quit.Clicked += (_, _) => Application.Current?.Quit();

        Content = new VerticalStackLayout
        {
            Spacing = 20,
            VerticalOptions = LayoutOptions.Center,
            Children = { heading, detail, quit }
        };
    }
}
