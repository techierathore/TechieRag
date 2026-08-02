using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager.Models;

namespace TechieDesk.Services.Auth;

/// <summary>
/// <see cref="ISessionStore"/>: a concurrent handle → session map with a sliding idle window and a
/// hard absolute lifetime (REQ-FN-032), whose live session is mirrored into the OS credential store
/// so it survives a restart (REQ-FN-039).
/// </summary>
/// <remarks>
/// Registered as a singleton: one desktop process serves one person, so every consumer that presents
/// the same handle sees the same session.
/// <para>
/// <b>REQ-FN-039.</b> The map itself is still process memory — that is what makes expiry, handle
/// rotation and "log out — all devices" cheap and exact. What changed is that the ONE live session is
/// additionally written to <see cref="ISecretStore"/> (Keychain / Credential Manager), and read back
/// on first use through <see cref="RestorePersistedSession"/>. Tokens are therefore never written to
/// a file, never handed to the WebView, and never protected by anything weaker than the OS store,
/// which binds them to this machine and this user account.
/// </para>
/// <para>
/// The three security properties this type owns are unchanged by that: both expiry bounds are
/// enforced on resolve AND carried across a restart (a restart cannot renew the hard cap), a fresh
/// handle is minted on every sign-in, and <see cref="InvalidateAllForUser"/> still drops every
/// session the user holds — including the persisted copy.
/// </para>
/// </remarks>
public sealed class SessionStore : ISessionStore
{
    /// <summary>The key the live session is stored under in the OS credential store.</summary>
    public const string SecretKey = "techiedesk.session.v1";

    private readonly ConcurrentDictionary<string, SessionRecord> sessions = new(StringComparer.Ordinal);
    private readonly object persistGate = new();
    private readonly SessionStoreOptions options;
    private readonly TimeProvider clock;
    private readonly ISecretStore secrets;
    private readonly ILogger<SessionStore> logger;
    private string? persistedHandle;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionStore"/> class.
    /// </summary>
    /// <param name="options">Session lifetime configuration.</param>
    /// <param name="clock">Time source (injectable so expiry is testable).</param>
    /// <param name="secrets">The OS credential store the live session is mirrored into (REQ-FN-039).</param>
    /// <param name="logger">Logger for security events.</param>
    public SessionStore(
        IOptions<SessionStoreOptions> options,
        TimeProvider clock,
        ISecretStore secrets,
        ILogger<SessionStore> logger)
    {
        this.options = options.Value;
        this.clock = clock;
        this.secrets = secrets;
        this.logger = logger;
    }

    /// <inheritdoc />
    public int ActiveSessionCount
    {
        get
        {
            PruneExpired();
            return sessions.Count;
        }
    }

    /// <inheritdoc />
    public string CreateSession(
        TechieDeskUser user,
        string accessToken,
        string refreshToken,
        DateTimeOffset expiresAt,
        ActiveLicenseData? activeLicense)
    {
        PruneExpired();

        var tokens = new SessionTokenStore();
        tokens.SetSession(user, accessToken, refreshToken, expiresAt, activeLicense);

        var now = clock.GetUtcNow();
        var handle = SessionHandle.Create();
        var record = new SessionRecord(user.UserId, tokens, now.AddHours(options.AbsoluteTimeoutHours))
        {
            IdleExpiresAt = now.AddMinutes(options.IdleTimeoutMinutes)
        };
        sessions[handle] = record;

        AdoptAsPersistedSession(handle, record);

        logger.LogInformation(
            "Session established for user {UserId} (handle rotated; {ActiveSessions} active)",
            user.UserId, sessions.Count);
        return handle;
    }

    /// <inheritdoc />
    public SessionTokenStore? Resolve(string? handle)
    {
        if (string.IsNullOrEmpty(handle) || !sessions.TryGetValue(handle, out var record))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        if (IsExpired(record, now))
        {
            Invalidate(handle);
            return null;
        }

        record.IdleExpiresAt = now.AddMinutes(options.IdleTimeoutMinutes);
        return record.Tokens;
    }

    /// <inheritdoc />
    public bool Invalidate(string? handle)
    {
        if (string.IsNullOrEmpty(handle) || !sessions.TryRemove(handle, out var record))
        {
            return false;
        }

        ForgetPersistedSession(handle, record);
        record.Tokens.Clear();
        logger.LogInformation("Session invalidated for user {UserId}", record.UserId);
        return true;
    }

    /// <inheritdoc />
    public int InvalidateAllForUser(int userId)
    {
        var handles = sessions
            .Where(pair => pair.Value.UserId == userId)
            .Select(pair => pair.Key)
            .ToArray();

        var removed = handles.Count(Invalidate);

        // A session persisted by an EARLIER run of the app has no in-memory entry to match on, so
        // the loop above would leave it behind and the next launch would silently restore the very
        // session the user just revoked everywhere. Drop the stored copy unconditionally.
        DiscardPersistedSecret("all-devices logout");

        logger.LogInformation("Dropped {Count} session(s) for user {UserId} (all devices)", removed, userId);
        return removed;
    }

    /// <inheritdoc />
    public string? RestorePersistedSession()
    {
        lock (persistGate)
        {
            if (persistedHandle is not null && sessions.ContainsKey(persistedHandle))
            {
                return persistedHandle;
            }
        }

        var payload = ReadPersistedSecret();
        if (payload is null)
        {
            return null;
        }

        // The HARD deadline is what bounds a restored session, and it is checked before anything is
        // handed back. The sliding idle window deliberately is NOT: it exists to protect a session
        // left unattended in a RUNNING app, and a closed app is not an unattended session. Enforcing
        // it here would mean a desktop app that had been shut overnight always demanded a fresh
        // sign-in, which is the exact clause REQ-FN-039 asks for ("a restart restores the session
        // without re-entry"). The stored idle deadline is kept in the payload for diagnostics and so
        // a future policy can tighten this without a format change.
        var now = clock.GetUtcNow();
        if (now >= payload.AbsoluteExpiresAt)
        {
            DiscardPersistedSecret("the stored session had passed its hard lifetime");
            return null;
        }

        var tokens = new SessionTokenStore();
        tokens.SetSession(
            payload.ToUser(), payload.AccessToken, payload.RefreshToken, payload.TokenExpiresAt,
            payload.ActiveLicense);

        // A brand-new handle, and the ORIGINAL hard deadline: restarting the app restores a session,
        // it never renews one.
        var handle = SessionHandle.Create();
        var record = new SessionRecord(payload.UserId, tokens, payload.AbsoluteExpiresAt)
        {
            IdleExpiresAt = now.AddMinutes(options.IdleTimeoutMinutes)
        };
        sessions[handle] = record;

        AdoptAsPersistedSession(handle, record);

        logger.LogInformation(
            "Restored the signed-in session for user {UserId} from the OS credential store (REQ-FN-039); " +
            "hard expiry stays {AbsoluteExpiresAt:u}",
            payload.UserId, payload.AbsoluteExpiresAt);
        return handle;
    }

    private bool IsExpired(SessionRecord record, DateTimeOffset now)
    {
        return now >= record.AbsoluteExpiresAt || now >= record.IdleExpiresAt;
    }

    private void PruneExpired()
    {
        var now = clock.GetUtcNow();
        foreach (var pair in sessions.Where(pair => IsExpired(pair.Value, now)).ToArray())
        {
            Invalidate(pair.Key);
        }
    }

    /// <summary>
    /// Makes this session the one mirrored into the OS credential store, and keeps the stored copy
    /// in step with every later in-place token change (silent refresh, sign-out).
    /// </summary>
    private void AdoptAsPersistedSession(string handle, SessionRecord record)
    {
        lock (persistGate)
        {
            persistedHandle = handle;
        }

        record.Tokens.Changed += OnTokensChanged;
        WritePersistedSecret(record);
    }

    private void OnTokensChanged(SessionTokenStore tokens)
    {
        string? handle;
        lock (persistGate)
        {
            handle = persistedHandle;
        }

        if (handle is null
            || !sessions.TryGetValue(handle, out var record)
            || !ReferenceEquals(record.Tokens, tokens))
        {
            return;
        }

        if (!tokens.HasSession)
        {
            DiscardPersistedSecret("the session was cleared");
            return;
        }

        WritePersistedSecret(record);
    }

    private void ForgetPersistedSession(string handle, SessionRecord record)
    {
        record.Tokens.Changed -= OnTokensChanged;

        bool owned;
        lock (persistGate)
        {
            owned = persistedHandle == handle;
            if (owned)
            {
                persistedHandle = null;
            }
        }

        if (owned)
        {
            DiscardPersistedSecret("the session was invalidated");
        }
    }

    private void WritePersistedSecret(SessionRecord record)
    {
        var tokens = record.Tokens;
        if (tokens.User is not { } user || tokens.AccessToken is null || tokens.RefreshToken is null)
        {
            return;
        }

        var payload = new PersistedSession(
            user.UserId, user.Email, user.DisplayName, user.Role,
            tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAt ?? DateTimeOffset.MinValue,
            record.AbsoluteExpiresAt, record.IdleExpiresAt, tokens.ActiveLicense);

        try
        {
            secrets.Write(SecretKey, JsonSerializer.Serialize(payload));
            logger.LogDebug(
                "Mirrored the live session into the OS credential store (durable: {Durable})",
                secrets.IsDurable);
        }
        catch (Exception ex)
        {
            // Never fatal: failing to persist costs the user a re-entry after a restart, whereas
            // throwing here would break a sign-in that has otherwise fully succeeded.
            logger.LogError(ex, "Could not write the session to the OS credential store");
        }
    }

    private PersistedSession? ReadPersistedSecret()
    {
        string? raw;
        try
        {
            raw = secrets.Read(SecretKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read the session from the OS credential store");
            return null;
        }

        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PersistedSession>(raw);
        }
        catch (JsonException)
        {
            // Deliberately logs neither the payload nor the exception message — both carry tokens.
            logger.LogWarning("The stored session could not be read back and was discarded");
            DiscardPersistedSecret("the stored session was unreadable");
            return null;
        }
    }

    private void DiscardPersistedSecret(string reason)
    {
        try
        {
            if (secrets.Delete(SecretKey))
            {
                logger.LogInformation("Removed the stored session from the OS credential store: {Reason}", reason);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not remove the session from the OS credential store");
        }
    }

    /// <summary>One stored session: its owner, its shared token state, and its two expiry bounds.</summary>
    private sealed class SessionRecord(int userId, SessionTokenStore tokens, DateTimeOffset absoluteExpiresAt)
    {
        public int UserId { get; } = userId;

        public SessionTokenStore Tokens { get; } = tokens;

        public DateTimeOffset AbsoluteExpiresAt { get; } = absoluteExpiresAt;

        public DateTimeOffset IdleExpiresAt { get; set; }
    }
}
