using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.Auth;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-FN-032 and REQ-UI-007 as they are actually built on the Mac Catalyst head, re-scoped from the
/// retired Blazor Server design (2026-07-28).
/// </summary>
/// <remarks>
/// <para>
/// The rows these tests serve used to be about SignalR circuits and a signed <c>td.sid</c> cookie.
/// REQ-FN-035 removed the entire HTTP boundary — Kestrel, circuits, the cookie scheme and
/// <c>POST /auth/login</c> — so those mechanisms cannot be exercised and, more importantly, cannot
/// fail. What replaces "session continuity across circuits" on a desktop head is continuity across
/// the two boundaries that DO still exist: a new component service scope (a WebView reload or a root
/// component rebuild) and a process restart.
/// </para>
/// <para>
/// The security property both rows were really protecting is unchanged and is asserted here in its
/// desktop form: the WebView renders a principal, and that principal must carry no token material and
/// not even the opaque session handle. The old "tokens never leave the server" (REQ-NFR-004) becomes
/// "tokens never leave the .NET process" — they live in <see cref="SessionTokenStore"/> and, when the
/// platform allows, in the OS credential store behind <see cref="ISecretStore"/>.
/// </para>
/// <para>
/// The complementary restart path (OS credential store round trip, hard-expiry carry-over, all-devices
/// revocation) is covered by <see cref="SecretStoreSessionTests"/> and is not duplicated here.
/// </para>
/// </remarks>
public sealed class DesktopSessionContinuityTests
{
    private const string AccessToken = "access-token-VERYSECRET-desktop";
    private const string RefreshToken = "refresh-token-VERYSECRET-desktop";

    /// <summary>
    /// The desktop replacement for "a replacement circuit adopts the session". A new component scope
    /// — what a WebView reload or a root-component rebuild produces — sees the SAME signed-in session
    /// with nothing adopted, handed over or re-entered, because the session is app-wide state rather
    /// than per-connection state.
    /// </summary>
    [Fact]
    public async Task NewComponentScopeKeepsTheSignedInSession()
    {
        using var services = DesktopServices();
        var handle = SignInThroughTheStore(services);

        // Scope one: the window as first rendered.
        using (var firstRender = services.CreateScope())
        {
            var state = await AuthStateOf(firstRender);
            Assert.Equal("user123@example.com", state.User.FindFirstValue(ClaimTypes.Email));
        }

        // Scope two: the WebView reloaded. Under the retired head this was a destroyed circuit and
        // the point at which the session used to be lost (the five-day login loop).
        using var afterReload = services.CreateScope();
        var reloaded = await AuthStateOf(afterReload);

        Assert.Equal("user123@example.com", reloaded.User.FindFirstValue(ClaimTypes.Email));
        Assert.Equal(handle, services.GetRequiredService<ISessionContext>().Handle);
        Assert.True(services.GetRequiredService<IRouteGuard>().IsSignedIn);
    }

    /// <summary>
    /// The structural reason the above holds, pinned so it cannot be undone by a lifetime change: the
    /// session store and the session context are SINGLETONS. Registered per scope they would be
    /// per-WebView-scope state, which is precisely the shape of the defect REQ-FN-032 opened for.
    /// </summary>
    [Fact]
    public void SessionIsAppWideNotPerScope()
    {
        var services = DesktopServiceCollection();

        Assert.Equal(ServiceLifetime.Singleton, LifetimeOf(services, typeof(ISessionStore)));
        Assert.Equal(ServiceLifetime.Singleton, LifetimeOf(services, typeof(ISessionContext)));
    }

    /// <summary>
    /// THE security invariant, in its desktop form. On a BlazorWebView the "browser" is in-process,
    /// so the meaningful boundary is the principal the render tree — and therefore the DOM and any
    /// script in the WebView — can observe. It carries identity, and nothing else: no access token,
    /// no refresh token, and not even the opaque handle that would resolve one.
    /// </summary>
    [Fact]
    public async Task RenderedPrincipalCarriesNoTokenMaterial()
    {
        using var services = DesktopServices();
        var handle = SignInThroughTheStore(services);

        using var scope = services.CreateScope();
        var state = await AuthStateOf(scope);
        var claims = state.User.Claims.ToArray();

        Assert.NotEmpty(claims);
        Assert.DoesNotContain(claims, claim => claim.Value.Contains(AccessToken, StringComparison.Ordinal));
        Assert.DoesNotContain(claims, claim => claim.Value.Contains(RefreshToken, StringComparison.Ordinal));
        Assert.DoesNotContain(claims, claim => claim.Value.Contains(handle, StringComparison.Ordinal));
        Assert.DoesNotContain(
            claims, claim => claim.Type.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The other half of that invariant: the tokens are still there, still resolvable server-side by
    /// the code that calls AppManager. The principal being empty of them must be because they never
    /// crossed the boundary, not because the session lost them.
    /// </summary>
    [Fact]
    public void TokensRemainResolvableInProcessOnly()
    {
        using var services = DesktopServices();
        SignInThroughTheStore(services);

        var tokens = services.GetRequiredService<ISessionContext>().Tokens;

        Assert.Equal(AccessToken, tokens.AccessToken);
        Assert.Equal(RefreshToken, tokens.RefreshToken);
    }

    /// <summary>
    /// The retired design is gone from the composition root, not merely unused: nothing registers an
    /// HTTP context accessor or an ASP.NET Core authentication scheme, so there is no cookie to
    /// write, steal or forge a request against on this head.
    /// </summary>
    [Fact]
    public void NoHttpOrCookieMachineryIsRegistered()
    {
        var services = DesktopServiceCollection();

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.Name.Contains("HttpContext", StringComparison.Ordinal));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.Namespace?.StartsWith(
                "Microsoft.AspNetCore.Authentication", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.Name.Contains("Antiforgery", StringComparison.Ordinal));
    }

    /// <summary>
    /// The honest limit of restart continuity, asserted rather than assumed. Persistence rides
    /// entirely on <see cref="ISecretStore"/>; where the OS store refuses the process the session is
    /// held in memory for that run only, and <see cref="ISecretStore.IsDurable"/> is the flag that
    /// says so. This is the state an unsigned Mac Catalyst build is actually in — Keychain answers
    /// <c>MissingEntitlement</c> — so a verifier reading "the session survives a relaunch" must check
    /// this flag before concluding the clause failed.
    /// </summary>
    [Fact]
    public void ANonDurableSecretStoreLosesTheSessionOnRestartAndSaysSo()
    {
        var secrets = new EphemeralSecretStore();
        SessionTestHarness.Store(secrets: secrets).CreateSession(
            SessionTestHarness.User(), AccessToken, RefreshToken,
            DateTimeOffset.UtcNow.AddHours(1), null);

        Assert.False(secrets.IsDurable);

        // A restart with a NON-durable store: the process is new, so the in-memory store is empty.
        // Nothing was written to disk, which is the property that must never be traded for
        // persistence (REQ-FN-039).
        var afterRestart = SessionTestHarness.Store(secrets: new EphemeralSecretStore());

        Assert.Null(afterRestart.RestorePersistedSession());
        Assert.False(new DesktopSessionContext(afterRestart).Tokens.HasSession);
    }

    /// <summary>
    /// REQ-UI-007 on an install with no licence server — the normal case under BRD-129, and the only
    /// sign-in path that can be driven on a host with no AppManager. The attempt is refused locally,
    /// no credential is put on the wire, and no session is left behind for the app to act on.
    /// </summary>
    [Fact]
    public async Task SignInWithNoLicenceServerIsRefusedLocallyAndEstablishesNothing()
    {
        var store = SessionTestHarness.Store();
        var context = new DesktopSessionContext(store);

        // The fake throws if it is called at all, so reaching the network fails the test.
        var service = new DesktopSignInService(
            new FakeAppManagerClient(),
            store,
            context,
            new TechieDeskAuthModeProvider(
                Options.Create(new AppManagerOptions { BaseUrl = string.Empty }),
                NullLogger<TechieDeskAuthModeProvider>.Instance),
            NullLogger<DesktopSignInService>.Instance);

        var outcome = await service.SignInAsync("admin@appmanager.local", "Admin@123!");

        Assert.False(outcome.Succeeded);
        Assert.Equal(AuthScreenCodes.NoLicenceServer, outcome.ErrorCode);
        Assert.False(context.Tokens.HasSession);
        Assert.Equal(0, store.ActiveSessionCount);
    }

    /// <summary>
    /// REQ-UI-007: the post-sign-in navigation is filtered before the screen uses it. The redirect
    /// round trip died with the HTTP endpoint, but <c>/login?returnUrl=</c> is still a real query
    /// parameter a deep link can set, so the open-redirect guard still has to hold.
    /// </summary>
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData("//evil.example/steal", "/")]
    [InlineData("https://evil.example/steal", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("/workspace/acme", "/workspace/acme")]
    public void ReturnUrlIsFilteredToAnInAppPath(string? candidate, string expected)
    {
        Assert.Equal(expected, AuthScreenCodes.SafeReturnUrl(candidate));
    }

    /// <summary>
    /// Signing out is app-wide too: the next component scope sees the local owner again rather than
    /// a stale AppManager identity, so the shell offers "Sign in" and not "Log out".
    /// </summary>
    [Fact]
    public async Task SignOutIsVisibleToEveryLaterScope()
    {
        using var services = DesktopServices();
        var handle = SignInThroughTheStore(services);

        services.GetRequiredService<ISessionStore>().Invalidate(handle);

        using var scope = services.CreateScope();
        var state = await AuthStateOf(scope);

        Assert.Equal(TechieDeskUser.BuiltInAdmin.Email, state.User.FindFirstValue(ClaimTypes.Email));
        Assert.False(services.GetRequiredService<IRouteGuard>().IsSignedIn);
    }

    /// <summary>
    /// Builds the real desktop service graph, exactly as <c>MauiProgram</c> composes it.
    /// </summary>
    /// <returns>The built provider.</returns>
    private static ServiceProvider DesktopServices()
    {
        return DesktopServiceCollection().BuildServiceProvider();
    }

    /// <summary>
    /// Builds the real desktop service collection: <c>AddTechieDeskDesktopAuth</c> over an empty
    /// configuration, which is the account-free install BRD-129 makes the normal case.
    /// </summary>
    /// <returns>The populated collection.</returns>
    private static IServiceCollection DesktopServiceCollection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTechieDeskDesktopAuth(configuration);
        return services;
    }

    /// <summary>Establishes a session the way <c>DesktopSignInService</c> does, without a network.</summary>
    /// <param name="services">The desktop service provider.</param>
    /// <returns>The opaque handle the app-wide context now presents.</returns>
    private static string SignInThroughTheStore(IServiceProvider services)
    {
        var handle = services.GetRequiredService<ISessionStore>().CreateSession(
            SessionTestHarness.User(), AccessToken, RefreshToken,
            DateTimeOffset.UtcNow.AddHours(1), null);
        services.GetRequiredService<ISessionContext>().AttachHandle(handle);
        return handle;
    }

    /// <summary>Reads the authentication state a component in this scope would be handed.</summary>
    /// <param name="scope">The component service scope.</param>
    /// <returns>The authentication state.</returns>
    private static Task<AuthenticationState> AuthStateOf(IServiceScope scope)
    {
        return scope.ServiceProvider
            .GetRequiredService<AuthenticationStateProvider>()
            .GetAuthenticationStateAsync();
    }

    /// <summary>Finds the registered lifetime of a service type.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceType">The service type to look up.</param>
    /// <returns>The registered lifetime.</returns>
    private static ServiceLifetime LifetimeOf(IServiceCollection services, Type serviceType)
    {
        return services.Last(descriptor => descriptor.ServiceType == serviceType).Lifetime;
    }
}
