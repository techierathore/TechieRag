namespace TechieDesk.Services.Auth;

/// <summary>
/// Lifetime configuration for the server-side session store (REQ-FN-032), bound from the
/// <c>Session</c> configuration section.
/// </summary>
/// <remarks>
/// The idle window is deliberately longer than an AppManager access token: expiry of the access
/// token is handled by <see cref="TokenRefresher"/> (silent refresh, REQ-FN-002), while these
/// values bound how long a browser may be away before its handle stops resolving at all.
/// </remarks>
public sealed class SessionStoreOptions
{
    /// <summary>Name of the configuration section this options class binds to.</summary>
    public const string SectionName = "Session";

    /// <summary>
    /// Sliding idle timeout, in minutes. A session that is not touched within this window is
    /// dropped and its handle stops resolving.
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// Absolute lifetime, in hours. A session is dropped once this elapses regardless of
    /// activity, so a stolen handle can never be used indefinitely.
    /// </summary>
    public int AbsoluteTimeoutHours { get; set; } = 12;
}
