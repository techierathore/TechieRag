using Foundation;

namespace TechieRag.Probe;

/// <summary>
/// Application delegate for this Apple head.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    /// <inheritdoc/>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
