namespace TechieDesk.Services.Auth;

/// <summary>
/// The authentication mode TechieDesk is running in (BRD-54).
/// </summary>
public enum TechieDeskAuthMode
{
    /// <summary>
    /// No AppManager base URL configured: offline single-user mode. No login is required and
    /// the current user is the built-in Admin.
    /// </summary>
    Offline,

    /// <summary>
    /// AppManager is configured: full authentication, route protection, and role checks apply.
    /// </summary>
    AppManager
}
