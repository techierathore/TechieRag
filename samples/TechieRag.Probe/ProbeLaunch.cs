namespace TechieRag.Probe;

/// <summary>
/// How the probe was launched: whether it presses one of its buttons once the page appears.
/// </summary>
/// <remarks>
/// <para>For unattended runs (the Android emulator job in CI, REQ-FN-057; the Mac smokes): set the
/// environment variable <see cref="AutoRunVariable"/> to <c>1</c> to press <b>Embed, store, search</b>,
/// or to <see cref="AutoRunLocalValue"/> to press <b>Generate one sentence</b> (REQ-FN-059). On Android
/// start the activity with the boolean extra <c>autorun</c> or <c>autorunlocal</c>
/// (<c>adb shell am start -n … --ez autorunlocal true</c>). The result line is then written to the
/// console (logcat on Android) prefixed <see cref="ResultPrefix"/>, and to <c>probe-result.txt</c> in
/// the app data folder.</para>
/// <para>Auto-run presses the button only. A first local-model run still shows the model's terms
/// dialog and waits for <b>Accept</b> (REQ-RAG-062); an unattended run answers it through the app's
/// own window (AutomationId-level automation), never by skipping it.</para>
/// </remarks>
public static class ProbeLaunch
{
    /// <summary>The environment variable that turns auto-run on.</summary>
    public const string AutoRunVariable = "TECHIERAG_PROBE_AUTORUN";

    /// <summary>The value of <see cref="AutoRunVariable"/> that presses the local-model button.</summary>
    public const string AutoRunLocalValue = "local";

    /// <summary>The prefix of the result line an unattended run greps for.</summary>
    public const string ResultPrefix = "TECHIERAG_PROBE_RESULT:";

    /// <summary>Gets or sets whether the embed button is pressed automatically; Android's activity sets it from its intent.</summary>
    public static bool AutoRun { get; set; } = Environment.GetEnvironmentVariable(AutoRunVariable) == "1";

    /// <summary>Gets or sets whether the local-model button is pressed automatically; Android's activity sets it from its intent.</summary>
    public static bool AutoRunLocal { get; set; } = Environment.GetEnvironmentVariable(AutoRunVariable) == AutoRunLocalValue;
}
