namespace TechieRag.Probe.WinUI;

/// <summary>
/// Windows head entry point.
/// </summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>Creates the WinUI application.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
