namespace TechieDesk.Services.Auth;

/// <summary>
/// <see cref="ISessionContext"/> for the MAUI Blazor Hybrid desktop head (REQ-FN-035).
/// </summary>
/// <remarks>
/// The retired <c>SessionContext</c> read its handle from <c>IHttpContextAccessor</c>, falling back
/// to a handle attached at circuit start. Neither exists here: a desktop install is one process
/// serving one person, so the "current session" is simply app-wide state. That is why this type is
/// registered as a <b>singleton</b> where its predecessor was scoped — there are no request or
/// circuit boundaries left for a scope to track.
/// <para>
/// This deliberately keeps the <see cref="ISessionStore"/> indirection rather than collapsing
/// straight to a <see cref="SessionTokenStore"/> field. The store already owns expiry, handle
/// rotation on login (session fixation) and "log out — all devices", and discarding that to save one
/// hop would quietly drop three security properties the Blazor head had. REQ-FN-039 replaces the
/// store's <i>persistence</i> with the OS credential store; it does not remove this seam.
/// </para>
/// <para>
/// REQ-FN-039: the first read of either member restores whatever session the OS credential store is
/// holding, which is what makes "a restart restores the session without re-entry" true no matter
/// which service asks first. It is done lazily rather than in a startup hook so nothing has to
/// remember to call it, and exactly once so a signed-out user is not repeatedly probed.
/// </para>
/// </remarks>
public sealed class DesktopSessionContext : ISessionContext
{
    private readonly ISessionStore sessionStore;
    private readonly object gate = new();
    private string? handle;
    private bool restoreAttempted;

    /// <summary>Creates a desktop session context over the supplied store.</summary>
    /// <param name="sessionStore">The session store holding the live session state.</param>
    public DesktopSessionContext(ISessionStore sessionStore)
    {
        this.sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    }

    /// <inheritdoc />
    public string? Handle
    {
        get
        {
            EnsureRestored();
            lock (gate)
            {
                return handle;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A handle whose session has expired resolves to null. When that happens the handle is cleared
    /// rather than left dangling, so the app presents as signed out instead of repeatedly probing a
    /// dead handle, and the caller still receives a non-null detached store.
    /// </remarks>
    public SessionTokenStore Tokens
    {
        get
        {
            EnsureRestored();

            string? current;
            lock (gate)
            {
                current = handle;
            }

            if (current is null)
            {
                return new SessionTokenStore();
            }

            var resolved = sessionStore.Resolve(current);
            if (resolved is not null)
            {
                return resolved;
            }

            lock (gate)
            {
                if (handle == current)
                {
                    handle = null;
                }
            }

            return new SessionTokenStore();
        }
    }

    /// <inheritdoc />
    public void AttachHandle(string? handle)
    {
        lock (gate)
        {
            // An explicit attach (sign-in, sign-out) is the authoritative answer for this process,
            // so it also settles the restore question — otherwise a later first read could restore a
            // stale stored session over the top of a fresh one.
            restoreAttempted = true;
            this.handle = handle;
        }
    }

    /// <summary>
    /// Restores the persisted session on first use, once (REQ-FN-039).
    /// </summary>
    private void EnsureRestored()
    {
        lock (gate)
        {
            if (restoreAttempted)
            {
                return;
            }

            restoreAttempted = true;
        }

        // Outside the lock: the store takes its own locks and this must not nest them.
        var restored = sessionStore.RestorePersistedSession();
        if (restored is null)
        {
            return;
        }

        lock (gate)
        {
            handle ??= restored;
        }
    }
}
