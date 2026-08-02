using System.Globalization;

namespace TechieDesk.Services.Install;

/// <summary>
/// Carries the launch-time single-instance decision to whatever presents it (REQ-FN-051 clause 3).
/// </summary>
/// <remarks>
/// <para>
/// The guard has to run in the composition root, before anything opens the database, but the thing
/// that must SHOW a refusal is the window. This is the one value passed between them. Registered as
/// a singleton so the head can take it as an ordinary constructor dependency rather than reaching
/// for a static.
/// </para>
/// <para>
/// <b>REQ-UI-055 (BRD-91): resource KEYS, and what a refused instance can actually resolve them
/// with.</b> The refusal used to be English text built here, and the reason recorded on
/// <c>SecondInstancePage</c> was that a refused instance never reads the stored UI language, so
/// there was "no localized culture in place to read from". Half of that is still true and half is
/// not. <c>MauiProgram</c> calls <c>RegisterAppServices</c> — and therefore
/// <c>AddTechieDeskAppearance</c>'s <c>AddLocalization</c> — and calls <c>builder.Build()</c>
/// BEFORE its <c>IsPrimaryInstance</c> early return, so <c>IStringLocalizer&lt;AppStrings&gt;</c>
/// IS resolvable in this path. What is skipped is <c>ApplyStoredLanguage</c>, which reads the app
/// database the live copy is writing and must stay skipped. So the refusal now resolves against
/// <c>CultureInfo.CurrentUICulture</c>, which on a desktop head is the OPERATING SYSTEM's language:
/// a machine running in Hindi gets a Hindi refusal without this process opening a single file. Only
/// the case where the OS language and the in-app language differ still falls back to the OS —
/// which is a strictly better answer than the English it used to be, and the only one available
/// without the database.
/// </para>
/// </remarks>
public sealed class SingleInstanceState
{
    /// <summary>Initializes a new instance of the <see cref="SingleInstanceState"/> class.</summary>
    /// <param name="result">The outcome recorded at launch.</param>
    public SingleInstanceState(SingleInstanceResult result)
    {
        Result = result;
    }

    /// <summary>Gets the outcome recorded at launch.</summary>
    public SingleInstanceResult Result { get; }

    /// <summary>Gets a value indicating whether this process may open the application window.</summary>
    public bool IsPrimaryInstance => Result.IsPrimaryInstance;

    /// <summary>Resource key for the title shown when a second instance refuses to start.</summary>
    /// <remarks>
    /// A refusal must be SEEN. A second instance that exits silently is indistinguishable from a
    /// crash, and the user's next move is to try again — so this text, not a quiet <c>return</c>, is
    /// the deliverable half of the guard.
    /// </remarks>
    public const string RefusalTitleKey = "SecondInstanceRefusalTitle";

    /// <summary>Resource key for the refusal shown when the owning process could be identified.</summary>
    /// <remarks>Placeholders: <c>{0}</c> owner process id, <c>{1}</c> data directory.</remarks>
    public const string RefusalMessageWithOwnerKey = "SecondInstanceRefusalMessageWithOwner";

    /// <summary>Resource key for the refusal shown when the owning process is unknown.</summary>
    /// <remarks>Placeholder: <c>{0}</c> data directory.</remarks>
    public const string RefusalMessageKey = "SecondInstanceRefusalMessage";

    /// <summary>Resource key for the button that closes the refused copy.</summary>
    public const string RefusalCloseButtonKey = "SecondInstanceCloseButton";

    /// <summary>Gets the resource key for the explanation this refusal deserves.</summary>
    /// <remarks>
    /// Two keys rather than one with an optional clause: an optional parenthetical inside a
    /// translated sentence is exactly what a translator cannot reorder, and Hindi puts the
    /// parenthetical somewhere English does not.
    /// </remarks>
    public string RefusalDetailKey =>
        Result.OwnerProcessId is null ? RefusalMessageKey : RefusalMessageWithOwnerKey;

    /// <summary>
    /// Gets the values <see cref="RefusalDetailKey"/>'s placeholders take, in order.
    /// </summary>
    /// <returns>The owner process id, when known, followed by the data directory.</returns>
    /// <remarks>
    /// Both are machine values — a PID and an absolute path — and are identical in every culture.
    /// </remarks>
    public IReadOnlyList<string> RefusalDetailArguments =>
        Result.OwnerProcessId is { } processId
            ? [processId.ToString(CultureInfo.InvariantCulture), Result.DataDirectory]
            : [Result.DataDirectory];
}
