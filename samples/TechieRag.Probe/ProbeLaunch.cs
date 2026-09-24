namespace TechieRag.Probe;

/// <summary>
/// How the probe was launched: whether it presses its own button once the page appears.
/// </summary>
/// <remarks>
/// For unattended runs (the Android emulator job in CI, REQ-FN-057): set the environment variable
/// <see cref="AutoRunVariable"/> to <c>1</c>, or on Android start the activity with the boolean extra
/// <c>autorun</c> (<c>adb shell am start -n … --ez autorun true</c>). The result line is then written
/// to the console (logcat on Android) prefixed <see cref="ResultPrefix"/>, and to
/// <c>probe-result.txt</c> in the app data folder.
/// </remarks>
public static class ProbeLaunch
{
    /// <summary>The environment variable that turns auto-run on.</summary>
    public const string AutoRunVariable = "TECHIERAG_PROBE_AUTORUN";

    /// <summary>The prefix of the result line an unattended run greps for.</summary>
    public const string ResultPrefix = "TECHIERAG_PROBE_RESULT:";

    /// <summary>Gets or sets whether the button is pressed automatically; Android's activity sets it from its intent.</summary>
    public static bool AutoRun { get; set; } = Environment.GetEnvironmentVariable(AutoRunVariable) == "1";
}
