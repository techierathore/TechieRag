namespace TechieDesk.Services.Auth;

/// <summary>
/// TechieDesk product roles, mapped from the AppManager app-scoped <c>applicationRole</c>
/// per BRD §5 (BRD-23). Ordered least- to most-privileged.
/// </summary>
public enum ProductRole
{
    /// <summary>Chat in assigned workspaces, own threads/history, own profile, billing, support.</summary>
    User = 0,

    /// <summary>Workspace, document, and connector management plus everything a User can do.</summary>
    Manager = 1,

    /// <summary>Instance settings, admin console, all workspaces — every capability.</summary>
    Admin = 2
}
