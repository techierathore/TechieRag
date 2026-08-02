namespace TechieDesk.Services.Updates;

/// <summary>
/// Ensures the automatic update check runs at most once per application launch (REQ-FN-038b).
/// </summary>
/// <remarks>
/// The layout that triggers the check is re-created on navigation, so without this the app would
/// check on every screen change — turning one opt-in call at launch into continuous polling of a
/// third party, which is precisely what REQ-NFR-008 objects to. Registered as a singleton; the
/// claim is interlocked because nothing guarantees the layout initialises on one thread only.
/// </remarks>
public sealed class UpdateLaunchState
{
    private int claimed;

    /// <summary>Claims the one automatic check allowed this launch.</summary>
    /// <returns>True for the first caller only.</returns>
    public bool TryClaimLaunchCheck() => Interlocked.Exchange(ref claimed, 1) == 0;
}
