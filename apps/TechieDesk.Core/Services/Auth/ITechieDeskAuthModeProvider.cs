namespace TechieDesk.Services.Auth;

/// <summary>
/// Exposes the active <see cref="TechieDeskAuthMode"/>. Everything that gates access
/// (route protection, capability checks, token refresh) consults this switch so the whole
/// auth stack degrades to offline single-user mode when AppManager is not configured.
/// </summary>
public interface ITechieDeskAuthModeProvider
{
    /// <summary>Gets the active authentication mode.</summary>
    TechieDeskAuthMode Mode { get; }

    /// <summary>Gets a value indicating whether AppManager-backed authentication is enabled.</summary>
    bool IsAppManagerEnabled { get; }
}
