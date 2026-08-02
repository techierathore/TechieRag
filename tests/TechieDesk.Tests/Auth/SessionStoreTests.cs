using System.Text.RegularExpressions;
using TechieDesk.Services.Auth;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-FN-032 / REQ-NFR-004: the handle-keyed session store — opaque handles that carry no token
/// material, rotation on login, resolution across separate circuits, invalidation on logout, and
/// both expiry bounds.
/// </summary>
public sealed class SessionStoreTests
{
    /// <summary>
    /// The handle is 256 bits of base64url randomness and leaks nothing: not the access token,
    /// not the refresh token, not the email, not the role. It is the ONLY value the browser ever
    /// receives, so this is the REQ-NFR-004 guarantee expressed as a test.
    /// </summary>
    [Fact]
    public void HandleIsOpaqueAndCarriesNoTokenMaterial()
    {
        var store = SessionTestHarness.Store();
        var user = SessionTestHarness.User();

        var handle = store.CreateSession(
            user, "access-token-secret", "refresh-token-secret", DateTimeOffset.UtcNow.AddHours(1), null);

        Assert.Matches(new Regex("^[A-Za-z0-9_-]{43}$"), handle);
        Assert.DoesNotContain("access-token-secret", handle, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-token-secret", handle, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Email, handle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.Role.ToString(), handle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.UserId.ToString(), handle, StringComparison.Ordinal);
    }

    /// <summary>Two sessions never share a handle, and handles are not predictable.</summary>
    [Fact]
    public void HandlesAreUniquePerSession()
    {
        var store = SessionTestHarness.Store();

        var handles = Enumerable.Range(0, 50)
            .Select(index => SessionTestHarness.SignIn(store, SessionTestHarness.User(index)))
            .ToArray();

        Assert.Equal(handles.Length, handles.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Session-fixation defence: signing in again mints a brand-new handle and the old one stops
    /// resolving once it is invalidated, so a pre-seeded cookie can never be promoted.
    /// </summary>
    [Fact]
    public void HandleRotatesOnLogin()
    {
        var store = SessionTestHarness.Store();
        var user = SessionTestHarness.User();
        var firstHandle = SessionTestHarness.SignIn(store, user);

        store.Invalidate(firstHandle);
        var secondHandle = SessionTestHarness.SignIn(store, user);

        Assert.NotEqual(firstHandle, secondHandle);
        Assert.Null(store.Resolve(firstHandle));
        Assert.NotNull(store.Resolve(secondHandle));
    }

    /// <summary>
    /// THE acceptance clause: two different circuits presenting the same handle resolve the SAME
    /// server-side session. This is what a full-page navigation (login redirect) and a browser
    /// refresh do, and it is what the per-circuit store could not survive.
    /// </summary>
    [Fact]
    public void SessionResolvesAcrossSeparateCircuits()
    {
        var store = SessionTestHarness.Store();
        var handle = SessionTestHarness.SignIn(store, SessionTestHarness.User(), "access-token-abc");

        var firstCircuit = SessionTestHarness.Circuit(store, handle);
        var secondCircuit = SessionTestHarness.Circuit(store, handle);

        Assert.True(firstCircuit.Tokens.HasSession);
        Assert.True(secondCircuit.Tokens.HasSession);
        Assert.Same(firstCircuit.Tokens, secondCircuit.Tokens);
        Assert.Equal("access-token-abc", secondCircuit.Tokens.AccessToken);
        Assert.Equal(123, secondCircuit.Tokens.User!.UserId);
    }

    /// <summary>A scope holding no handle — or an unknown one — sees a detached, empty store.</summary>
    [Fact]
    public void UnknownHandleResolvesToNoSession()
    {
        var store = SessionTestHarness.Store();
        SessionTestHarness.SignIn(store, SessionTestHarness.User());

        Assert.False(SessionTestHarness.Circuit(store, null).Tokens.HasSession);
        Assert.False(SessionTestHarness.Circuit(store, "not-a-real-handle").Tokens.HasSession);
    }

    // REQ-FN-035: SessionResolvesFromCookiePrincipalOnAnHttpRequest was removed here. It asserted
    // that the static-SSR pass discovers its handle from the signed cookie principal — the one
    // behaviour in this file with no desktop equivalent, since a MAUI head serves no HTTP requests
    // and writes no cookie. Every other test in this file survives unchanged against
    // DesktopSessionContext.

    /// <summary>Logout drops the entry, wipes the tokens, and stops the handle resolving.</summary>
    [Fact]
    public void LogoutInvalidatesTheSession()
    {
        var store = SessionTestHarness.Store();
        var handle = SessionTestHarness.SignIn(store, SessionTestHarness.User());
        var tokens = store.Resolve(handle)!;

        var removed = store.Invalidate(handle);

        Assert.True(removed);
        Assert.Null(store.Resolve(handle));
        Assert.False(tokens.HasSession);
        Assert.Null(tokens.AccessToken);
        Assert.Equal(0, store.ActiveSessionCount);
    }

    /// <summary>
    /// REQ-UI-008 "log out — all devices" drops every session for that user and leaves other
    /// users' sessions alone.
    /// </summary>
    [Fact]
    public void LogoutAllDevicesDropsEverySessionForTheUser()
    {
        var store = SessionTestHarness.Store();
        var user = SessionTestHarness.User(7);
        var deviceOne = SessionTestHarness.SignIn(store, user);
        var deviceTwo = SessionTestHarness.SignIn(store, user);
        var otherUser = SessionTestHarness.SignIn(store, SessionTestHarness.User(8));

        var removed = store.InvalidateAllForUser(7);

        Assert.Equal(2, removed);
        Assert.Null(store.Resolve(deviceOne));
        Assert.Null(store.Resolve(deviceTwo));
        Assert.NotNull(store.Resolve(otherUser));
    }

    /// <summary>A session left idle past the sliding window stops resolving.</summary>
    [Fact]
    public void IdleSessionExpires()
    {
        var clock = new SessionTestHarness.TestClock(DateTimeOffset.Parse("2026-07-25T10:00:00Z"));
        var store = SessionTestHarness.Store(clock, idleTimeoutMinutes: 30);
        var handle = SessionTestHarness.SignIn(store, SessionTestHarness.User());

        clock.Advance(TimeSpan.FromMinutes(31));

        Assert.Null(store.Resolve(handle));
        Assert.Equal(0, store.ActiveSessionCount);
    }

    /// <summary>Activity inside the idle window slides the expiry forward.</summary>
    [Fact]
    public void ActivitySlidesTheIdleWindow()
    {
        var clock = new SessionTestHarness.TestClock(DateTimeOffset.Parse("2026-07-25T10:00:00Z"));
        var store = SessionTestHarness.Store(clock, idleTimeoutMinutes: 30);
        var handle = SessionTestHarness.SignIn(store, SessionTestHarness.User());

        clock.Advance(TimeSpan.FromMinutes(20));
        Assert.NotNull(store.Resolve(handle));
        clock.Advance(TimeSpan.FromMinutes(20));

        Assert.NotNull(store.Resolve(handle));
    }

    /// <summary>
    /// The absolute cap wins over activity: a continuously used session is still dropped once its
    /// hard lifetime elapses, so a stolen handle cannot be renewed forever.
    /// </summary>
    [Fact]
    public void AbsoluteLifetimeExpiresEvenWhenActive()
    {
        var clock = new SessionTestHarness.TestClock(DateTimeOffset.Parse("2026-07-25T10:00:00Z"));
        var store = SessionTestHarness.Store(clock, idleTimeoutMinutes: 60, absoluteTimeoutHours: 2);
        var handle = SessionTestHarness.SignIn(store, SessionTestHarness.User());

        clock.Advance(TimeSpan.FromMinutes(50));
        Assert.NotNull(store.Resolve(handle));
        clock.Advance(TimeSpan.FromMinutes(50));
        Assert.NotNull(store.Resolve(handle));
        clock.Advance(TimeSpan.FromMinutes(50));

        Assert.Null(store.Resolve(handle));
    }
}
