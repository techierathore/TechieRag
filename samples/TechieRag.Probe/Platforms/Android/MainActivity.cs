using Android.App;
using Android.Content.PM;
using Android.OS;

namespace TechieRag.Probe;

/// <summary>
/// Android head's activity. The boolean intent extra <c>autorun</c> presses the probe's button once
/// the page appears (REQ-FN-057, the CI emulator run); <c>autorunlocal</c> presses the local-model
/// button (REQ-FN-059). The Java name is fixed so
/// <c>adb shell am start -n com.techierathore.techierag.probe/.MainActivity</c> is stable.
/// </summary>
[Activity(
    Name = "com.techierathore.techierag.probe.MainActivity",
    Theme = "@style/Maui.MainTheme.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <inheritdoc/>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (Intent?.GetBooleanExtra("autorun", false) == true)
        {
            ProbeLaunch.AutoRun = true;
        }

        if (Intent?.GetBooleanExtra("autorunlocal", false) == true)
        {
            ProbeLaunch.AutoRunLocal = true;
        }

        base.OnCreate(savedInstanceState);
    }
}
