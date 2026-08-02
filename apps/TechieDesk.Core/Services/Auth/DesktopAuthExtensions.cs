using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;

namespace TechieDesk.Services.Auth;

/// <summary>
/// DI wiring for TechieDesk's AppManager integration and identity services on the desktop head
/// (REQ-FN-035).
/// </summary>
/// <remarks>
/// Replaces the retired <c>TechieDeskAuthExtensions</c>, which is excluded from compilation because
/// it registered an ASP.NET Core cookie authentication scheme and returned <c>WebApplication</c>.
/// Everything that did not depend on the HTTP pipeline is carried over verbatim, so identity,
/// licensing and token refresh behave exactly as they did.
/// <para>
/// Two registrations changed shape, both because the desktop head has no request or circuit
/// boundary: <c>IHttpContextAccessor</c> is gone, and <see cref="ISessionContext"/> is now the
/// singleton <see cref="DesktopSessionContext"/> instead of a scoped cookie/circuit reader. The
/// cookie scheme itself has no desktop equivalent and is not replaced — REQ-FN-039 moves session
/// persistence to the OS credential store, and REQ-FN-041 deletes the cookie machinery from disk.
/// </para>
/// </remarks>
public static class DesktopAuthExtensions
{
    /// <summary>
    /// Registers the AppManager typed client, the auth-mode switch, the app-wide session, the custom
    /// <see cref="AuthenticationStateProvider"/>, and the identity services. With
    /// <c>AppManager:BaseUrl</c> empty the app runs offline as the built-in Admin (BRD-54).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddTechieDeskDesktopAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AppManagerOptions>(configuration.GetSection(AppManagerOptions.SectionName));
        services.Configure<SessionStoreOptions>(configuration.GetSection(SessionStoreOptions.SectionName));

        // AppManager wire client (REQ-FN-004) with the shared RSA public-key cache (REQ-FN-001).
        services.AddSingleton<IPublicKeyCache, PublicKeyCache>();
        services.AddHttpClient<IAppManagerClient, AppManagerClient>()
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                var handler = new HttpClientHandler();
                var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
                var appManagerOptions = serviceProvider.GetRequiredService<IOptions<AppManagerOptions>>().Value;

                // REQ-NFR-004: refuse to dial AppManager over cleartext. Credentials and JWTs ride
                // this channel, so a non-https base URL is a hard failure (loopback http is
                // tolerated in Development only).
                AppManagerTransportSecurity.EnsureSecureBaseUrl(
                    appManagerOptions.BaseUrl, environment.IsDevelopment());

                // DEVELOPMENT ONLY: trust a self-signed AppManager TLS certificate when explicitly
                // opted in (AppManager:AllowUntrustedServerCertificate). The Development guard means
                // this can never relax certificate validation in a shipped desktop build, and it is
                // scoped to the AppManager typed client alone — never a global/default handler.
                if (AppManagerTransportSecurity.ShouldAcceptUntrustedCertificate(
                        environment.IsDevelopment(), appManagerOptions.AllowUntrustedServerCertificate))
                {
                    handler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }

                return handler;
            });

        // Auth mode switch (REQ-FN-002).
        services.AddSingleton<ITechieDeskAuthModeProvider, TechieDeskAuthModeProvider>();

        services.TryAddSingleton(TimeProvider.System);

        // REQ-FN-039: the OS credential store (Keychain / Credential Manager) that JWT + refresh
        // tokens live in. The head registers the platform implementation before calling this; the
        // fallback below is for hosts that have none — the test project, and any future non-MAUI
        // consumer of these services. It keeps nothing on disk, so the confidentiality property
        // holds either way; what it cannot do is survive a restart, which it reports honestly via
        // ISecretStore.IsDurable.
        services.TryAddSingleton<ISecretStore, EphemeralSecretStore>();
        services.AddSingleton<ISessionStore, SessionStore>();

        // Singleton, not scoped: one desktop process serves one person, so the session is app-wide.
        // The store keeps owning expiry, handle rotation and all-devices logout (REQ-UI-008).
        services.AddSingleton<ISessionContext, DesktopSessionContext>();
        services.AddTransient<SessionTokenStore>(provider =>
            provider.GetRequiredService<ISessionContext>().Tokens);
        services.AddScoped<ITokenRefresher, TokenRefresher>();

        // REQ-FN-039 / REQ-UI-007: the in-process replacement for POST /auth/login and
        // /auth/register, which went with the web host and left the auth screens inert.
        services.AddScoped<IDesktopSignInService, DesktopSignInService>();

        // Identity (REQ-FN-003) and the route guard's surviving sign-in-state report.
        // REQ-FN-041 (2026-07-26): ICapabilityService and IAuthGuard are gone with the
        // role/capability matrix — REQ-FN-005/006/007 are N/A on a single-user desktop install,
        // where the person at the keyboard is always the local owner (built-in Admin).
        services.AddScoped<ITechieDeskUserContext, TechieDeskUserContext>();
        services.AddScoped<IRouteGuard, RouteGuard>();

        services.AddScoped<TechieDeskAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(provider =>
            provider.GetRequiredService<TechieDeskAuthenticationStateProvider>());
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();

        return services;
    }
}
