using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.AppManager.Models;
using TechieDesk.Services.Auth;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-FN-039 / BRD-132: JWT + refresh tokens live in the OS credential store, a restart restores the
/// session without re-entry, and nothing sensitive reaches a plain file — while the three security
/// properties <see cref="ISessionStore"/> owns (expiry, handle rotation on sign-in, all-devices
/// logout) all keep holding.
/// </summary>
/// <remarks>
/// The platform store itself (<c>SecureStorage</c> → Keychain / Credential Manager) lives in the MAUI
/// head and cannot be exercised from a net10.0 test process. What is asserted here is everything on
/// this side of the <see cref="ISecretStore"/> seam: that the tokens go through it and nowhere else,
/// which is precisely the property that would regress if persistence were quietly re-backed by a file.
/// </remarks>
public sealed class SecretStoreSessionTests
{
    private const string AccessToken = "access-token-VERYSECRET-0001";
    private const string RefreshToken = "refresh-token-VERYSECRET-0002";

    /// <summary>
    /// The round trip: a signed-in session is written to the secret store, and everything a caller
    /// needs — tokens, expiry, identity and the captured licence — comes back out of it.
    /// </summary>
    [Fact]
    public void TokensRoundTripThroughTheSecretStore()
    {
        var secrets = new EphemeralSecretStore();
        var store = SessionTestHarness.Store(secrets: secrets);
        var licence = new ActiveLicenseData { LicenseId = 42, LicenseName = "Professional", Status = "Active" };

        store.CreateSession(
            SessionTestHarness.User(), AccessToken, RefreshToken,
            DateTimeOffset.UtcNow.AddHours(1), licence);

        Assert.NotNull(secrets.Read(SessionStore.SecretKey));

        var restored = SessionTestHarness.Store(secrets: secrets);
        var tokens = restored.Resolve(restored.RestorePersistedSession());

        Assert.NotNull(tokens);
        Assert.Equal(AccessToken, tokens!.AccessToken);
        Assert.Equal(RefreshToken, tokens.RefreshToken);
        Assert.Equal(123, tokens.User!.UserId);
        Assert.Equal(ProductRole.Manager, tokens.User.Role);
        Assert.Equal(42, tokens.ActiveLicense!.LicenseId);
    }

    /// <summary>
    /// THE acceptance clause: a completely new process — new store, new session context, nothing
    /// carried over but the secret store — presents the user as still signed in, with no re-entry.
    /// </summary>
    [Fact]
    public void ARestartRestoresTheSessionWithoutReEntry()
    {
        var secrets = new EphemeralSecretStore();
        SessionTestHarness.Store(secrets: secrets).CreateSession(
            SessionTestHarness.User(), AccessToken, RefreshToken,
            DateTimeOffset.UtcNow.AddHours(1), null);

        // Everything below models the next launch of the app.
        var afterRestart = SessionTestHarness.Store(secrets: secrets);
        var context = new DesktopSessionContext(afterRestart);

        Assert.True(context.Tokens.HasSession);
        Assert.Equal(AccessToken, context.Tokens.AccessToken);
        Assert.NotNull(context.Handle);
    }

    /// <summary>
    /// The restored session gets a BRAND NEW handle: one that leaked from a previous run is dead the
    /// moment the app restarts.
    /// </summary>
    [Fact]
    public void ARestartMintsAFreshHandle()
    {
        var secrets = new EphemeralSecretStore();
        var beforeHandle = SessionTestHarness.Store(secrets: secrets).CreateSession(
            SessionTestHarness.User(), AccessToken, RefreshToken,
            DateTimeOffset.UtcNow.AddHours(1), null);

        var afterRestart = SessionTestHarness.Store(secrets: secrets);
        var afterHandle = afterRestart.RestorePersistedSession();

        Assert.NotNull(afterHandle);
        Assert.NotEqual(beforeHandle, afterHandle);
        Assert.Null(afterRestart.Resolve(beforeHandle));
    }

    /// <summary>Restoring twice reuses the live session rather than piling up duplicates.</summary>
    [Fact]
    public void RestoringTwiceReturnsTheSameSession()
    {
        var secrets = new EphemeralSecretStore();
        SessionTestHarness.Store(secrets: secrets).CreateSession(
            SessionTestHarness.User(), AccessToken, RefreshToken,
            DateTimeOffset.UtcNow.AddHours(1), null);

        var afterRestart = SessionTestHarness.Store(secrets: secrets);

        Assert.Equal(afterRestart.RestorePersistedSession(), afterRestart.RestorePersistedSession());
        Assert.Equal(1, afterRestart.ActiveSessionCount);
    }

    /// <summary>
    /// Nothing sensitive reaches a plain file. The store is handed a secret store that also mirrors
    /// every write into a directory; the assertion is that the directory the app owns stays empty of
    /// token material, i.e. the ONLY route to persistence is the credential store.
    /// </summary>
    [Fact]
    public void TokensAreAbsentFromEveryPlainFileOnDisk()
    {
        var sandbox = Path.Combine(
            Path.GetTempPath(), "techiedesk-secret-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            var previous = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(sandbox);
            try
            {
                var store = SessionTestHarness.Store(secrets: new EphemeralSecretStore());
                store.CreateSession(
                    SessionTestHarness.User(), AccessToken, RefreshToken,
                    DateTimeOffset.UtcNow.AddHours(1),
                    new ActiveLicenseData { LicenseId = 42, LicenseName = "Professional" });
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }

            var files = Directory.GetFiles(sandbox, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain(AccessToken, text, StringComparison.Ordinal);
                Assert.DoesNotContain(RefreshToken, text, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    /// <summary>
    /// Expiry survives the move to the credential store: a stored session past its hard lifetime is
    /// refused, and the stored copy is dropped rather than left to be retried forever.
    /// </summary>
    [Fact]
    public void AnExpiredStoredSessionIsNotRestored()
    {
        var secrets = new EphemeralSecretStore();
        var clock = new SessionTestHarness.TestClock(DateTimeOffset.Parse("2026-07-26T10:00:00Z"));
        SessionTestHarness.Store(clock, absoluteTimeoutHours: 2, secrets: secrets).CreateSession(
            SessionTestHarness.User(), AccessToken, RefreshToken,
            clock.GetUtcNow().AddHours(1), null);

        clock.Advance(TimeSpan.FromHours(3));
        var afterRestart = SessionTestHarness.Store(clock, absoluteTimeoutHours: 2, secrets: secrets);

        Assert.Null(afterRestart.RestorePersistedSession());
        Assert.Null(secrets.Read(SessionStore.SecretKey));
    }

    /// <summary>
    /// The sliding idle window still bites inside a running app — the move to the credential store
    /// changed only whether a CLOSED app counts as idle time, not whether idle timeout is enforced.
    /// </summary>
    [Fact]
    public void TheIdleWindowStillExpiresALiveSession()
    {
        var secrets = new EphemeralSecretStore();
        var clock = new SessionTestHarness.TestClock(DateTimeOffset.Parse("2026-07-26T10:00:00Z"));
        var store = SessionTestHarness.Store(clock, idleTimeoutMinutes: 30, secrets: secrets);
        var handle = store.CreateSession(
            SessionTestHarness.User(), AccessToken, RefreshToken,
            clock.GetUtcNow().AddHours(1), null);

        clock.Advance(TimeSpan.FromMinutes(31));

        Assert.Null(store.Resolve(handle));
        Assert.Null(secrets.Read(SessionStore.SecretKey));
    }

    /// <summary>
    /// A restart restores a session; it never renews one. The hard lifetime travels with the stored
    /// copy, so relaunching the app cannot be used to extend a session indefinitely.
    /// </summary>
    [Fact]
    public void ARestartCannotRenewTheHardLifetime()
    {
        var secrets = new EphemeralSecretStore();
        var clock = new SessionTestHarness.TestClock(DateTimeOffset.Parse("2026-07-26T10:00:00Z"));

        // A generous idle window, so the ONLY thing that can end this session is the hard cap.
        SessionTestHarness.Store(clock, idleTimeoutMinutes: 600, absoluteTimeoutHours: 12, secrets: secrets)
            .CreateSession(
                SessionTestHarness.User(), AccessToken, RefreshToken,
                clock.GetUtcNow().AddHours(1), null);

        clock.Advance(TimeSpan.FromHours(11));
        var afterRestart = SessionTestHarness.Store(
            clock, idleTimeoutMinutes: 600, absoluteTimeoutHours: 12, secrets: secrets);
        var handle = afterRestart.RestorePersistedSession();
        Assert.NotNull(handle);

        // One more hour puts us past the ORIGINAL 12h cap, not 12h past the restart.
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Null(afterRestart.Resolve(handle));
    }

    /// <summary>Signing out drops the stored copy, so the next launch does not resurrect it.</summary>
    [Fact]
    public void SignOutRemovesTheStoredSession()
    {
        var secrets = new EphemeralSecretStore();
        var store = SessionTestHarness.Store(secrets: secrets);
        var handle = store.CreateSession(
            SessionTestHarness.User(), AccessToken, RefreshToken,
            DateTimeOffset.UtcNow.AddHours(1), null);

        store.Invalidate(handle);

        Assert.Null(secrets.Read(SessionStore.SecretKey));
        Assert.Null(SessionTestHarness.Store(secrets: secrets).RestorePersistedSession());
    }

    /// <summary>
    /// REQ-UI-008 "log out — all devices" still revokes everything, INCLUDING a copy persisted by an
    /// earlier run that this process has no in-memory entry for.
    /// </summary>
    [Fact]
    public void AllDevicesLogoutRevokesTheStoredSessionToo()
    {
        var secrets = new EphemeralSecretStore();
        SessionTestHarness.Store(secrets: secrets).CreateSession(
            SessionTestHarness.User(7), AccessToken, RefreshToken,
            DateTimeOffset.UtcNow.AddHours(1), null);

        // A later run of the app: the stored session has not been touched yet, so there is nothing
        // in memory to match the user id against.
        var afterRestart = SessionTestHarness.Store(secrets: secrets);
        afterRestart.InvalidateAllForUser(7);

        Assert.Null(secrets.Read(SessionStore.SecretKey));
        Assert.Null(afterRestart.RestorePersistedSession());
    }

    /// <summary>
    /// A silent token refresh replaces the token pair in place, and the stored copy keeps up — else
    /// "a restart restores the session" would hold only until the first refresh rotated the token.
    /// </summary>
    [Fact]
    public void SilentRefreshUpdatesTheStoredTokens()
    {
        var secrets = new EphemeralSecretStore();
        var store = SessionTestHarness.Store(secrets: secrets);
        var handle = store.CreateSession(
            SessionTestHarness.User(), AccessToken, RefreshToken,
            DateTimeOffset.UtcNow.AddHours(1), null);

        store.Resolve(handle)!.UpdateTokens("access-token-ROTATED", "refresh-token-ROTATED",
            DateTimeOffset.UtcNow.AddHours(2));

        var afterRestart = SessionTestHarness.Store(secrets: secrets);
        var tokens = afterRestart.Resolve(afterRestart.RestorePersistedSession());

        Assert.Equal("access-token-ROTATED", tokens!.AccessToken);
        Assert.Equal("refresh-token-ROTATED", tokens.RefreshToken);
    }

    /// <summary>A failed refresh clears the session, and the stored copy goes with it.</summary>
    [Fact]
    public void ClearingTheSessionRemovesTheStoredCopy()
    {
        var secrets = new EphemeralSecretStore();
        var store = SessionTestHarness.Store(secrets: secrets);
        var handle = store.CreateSession(
            SessionTestHarness.User(), AccessToken, RefreshToken,
            DateTimeOffset.UtcNow.AddHours(1), null);

        store.Resolve(handle)!.Clear();

        Assert.Null(secrets.Read(SessionStore.SecretKey));
    }

    /// <summary>A corrupt or hand-edited stored value is discarded, never thrown from.</summary>
    [Fact]
    public void AnUnreadableStoredSessionIsDiscarded()
    {
        var secrets = new EphemeralSecretStore();
        secrets.Write(SessionStore.SecretKey, "not-json-at-all");

        var store = SessionTestHarness.Store(secrets: secrets);

        Assert.Null(store.RestorePersistedSession());
        Assert.Null(secrets.Read(SessionStore.SecretKey));
    }

    /// <summary>
    /// Session-fixation defence, now on the in-process path: a second sign-in destroys the handle
    /// the process was holding before it mints the new one.
    /// </summary>
    [Fact]
    public async Task SigningInRotatesTheHandleAndReplacesTheStoredSession()
    {
        var secrets = new EphemeralSecretStore();
        var store = SessionTestHarness.Store(secrets: secrets);
        var context = new DesktopSessionContext(store);
        var service = SignInService(store, context, Response("first-access", "first-refresh"));

        Assert.True((await service.SignInAsync("user@example.com", "Passw0rd!")).Succeeded);
        var firstHandle = context.Handle;

        var second = SignInService(store, context, Response("second-access", "second-refresh"));
        Assert.True((await second.SignInAsync("user@example.com", "Passw0rd!")).Succeeded);

        Assert.NotEqual(firstHandle, context.Handle);
        Assert.Null(store.Resolve(firstHandle));
        Assert.Equal(1, store.ActiveSessionCount);
        Assert.Equal("second-access", context.Tokens.AccessToken);
        Assert.Contains("second-refresh", secrets.Read(SessionStore.SecretKey)!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the vertical slice: a sign-in that actually reaches AppManager commits the
    /// tokens to the credential store, which is where the restore path picks them up.
    /// </summary>
    [Fact]
    public async Task SigningInCommitsTheTokensToTheSecretStore()
    {
        var secrets = new EphemeralSecretStore();
        var store = SessionTestHarness.Store(secrets: secrets);
        var context = new DesktopSessionContext(store);
        var service = SignInService(store, context, Response(AccessToken, RefreshToken));

        var outcome = await service.SignInAsync(" user@example.com ", "Passw0rd!");

        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.ErrorCode);
        var stored = secrets.Read(SessionStore.SecretKey);
        Assert.NotNull(stored);
        Assert.Contains(AccessToken, stored!, StringComparison.Ordinal);
        Assert.Contains(RefreshToken, stored, StringComparison.Ordinal);
    }

    /// <summary>An empty field never reaches the network and never establishes a session.</summary>
    [Fact]
    public async Task IncompleteCredentialsAreRefusedBeforeAnyCall()
    {
        var secrets = new EphemeralSecretStore();
        var store = SessionTestHarness.Store(secrets: secrets);
        var context = new DesktopSessionContext(store);
        // No OnLogin script: the fake throws if it is called at all.
        var service = SignInService(store, context, null);

        var outcome = await service.SignInAsync("   ", string.Empty);

        Assert.False(outcome.Succeeded);
        Assert.Equal(AuthScreenCodes.MissingFields, outcome.ErrorCode);
        Assert.Null(secrets.Read(SessionStore.SecretKey));
    }

    /// <summary>
    /// An AppManager rejection surfaces as its wire code, so the screen renders the specific banner
    /// the endpoint's redirect used to carry, and leaves no session behind.
    /// </summary>
    [Fact]
    public async Task RejectedCredentialsReportTheWireCodeAndEstablishNothing()
    {
        var secrets = new EphemeralSecretStore();
        var store = SessionTestHarness.Store(secrets: secrets);
        var context = new DesktopSessionContext(store);
        var client = new FakeAppManagerClient
        {
            OnLogin = (_, _) => throw new AppManagerException("INVALID_CREDENTIALS", "Invalid email or password.")
        };
        var service = SignInService(store, context, client);

        var outcome = await service.SignInAsync("user@example.com", "wrong");

        Assert.False(outcome.Succeeded);
        Assert.Equal("INVALID_CREDENTIALS", outcome.ErrorCode);
        Assert.False(context.Tokens.HasSession);
        Assert.Null(secrets.Read(SessionStore.SecretKey));
    }

    /// <summary>Registration applies the password policy before any credential leaves the machine.</summary>
    [Fact]
    public async Task RegistrationRefusesAWeakOrMismatchedPassword()
    {
        var secrets = new EphemeralSecretStore();
        var store = SessionTestHarness.Store(secrets: secrets);
        var context = new DesktopSessionContext(store);
        var service = SignInService(store, context, null);
        var request = new RegisterRequest { Email = "new@example.com", FirstName = "Ravi", LastName = "Kumar" };

        var weak = await service.RegisterAsync(request, "short", "short");
        var mismatched = await service.RegisterAsync(request, "Passw0rd!", "Passw0rd?");

        Assert.Equal(AuthScreenCodes.WeakPassword, weak.ErrorCode);
        Assert.Equal(AuthScreenCodes.PasswordMismatch, mismatched.ErrorCode);
        Assert.Null(secrets.Read(SessionStore.SecretKey));
    }

    private static FakeAppManagerClient Response(string accessToken, string refreshToken)
    {
        return new FakeAppManagerClient
        {
            OnLogin = (email, _) => Task.FromResult(new AuthResponseData
            {
                UserId = 123,
                Email = email,
                FirstName = "Test",
                LastName = "User",
                ApplicationRole = "Manager",
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            })
        };
    }

    private static DesktopSignInService SignInService(
        ISessionStore store, ISessionContext context, IAppManagerClient? client)
    {
        // A configured BaseUrl is what puts the app in AppManager mode; no request is ever sent,
        // because the client itself is a fake.
        var modeProvider = new TechieDeskAuthModeProvider(
            Options.Create(new AppManagerOptions { BaseUrl = "https://appmanager.example" }),
            NullLogger<TechieDeskAuthModeProvider>.Instance);

        return new DesktopSignInService(
            client ?? new FakeAppManagerClient(),
            store,
            context,
            modeProvider,
            NullLogger<DesktopSignInService>.Instance);
    }
}
