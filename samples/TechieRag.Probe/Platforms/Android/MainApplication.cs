using Android.App;
using Android.Runtime;

namespace TechieRag.Probe;

/// <summary>
/// Android head's application class.
/// </summary>
[Application]
public class MainApplication : MauiApplication
{
    /// <summary>Called by the Android runtime.</summary>
    /// <param name="handle">The Java object handle.</param>
    /// <param name="ownership">Handle ownership.</param>
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    /// <inheritdoc/>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
