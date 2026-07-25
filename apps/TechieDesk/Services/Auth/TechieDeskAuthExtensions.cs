using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;

namespace TechieDesk.Services.Auth;

/// <summary>
/// DI and pipeline wiring for TechieDesk's AppManager integration and authorization stack
/// (REQ-FN-001…007). Keeps <c>Program.cs</c> to a single registration line per concern.
/// </summary>
public static class TechieDeskAuthExtensions
{
    /// <summary>
    /// Registers the AppManager typed client, the auth-mode switch, the per-circuit session
    /// token store, the custom <see cref="AuthenticationStateProvider"/>, and the role /
    /// capability / guard services. When <c>AppManager:BaseUrl</c> is empty the app runs in
    /// offline single-user mode with the built-in Admin (BRD-54).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddTechieDeskAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppManagerOptions>(configuration.GetSection(AppManagerOptions.SectionName));

        // AppManager wire client (REQ-FN-004) with the shared RSA public-key cache (REQ-FN-001).
        services.AddSingleton<IPublicKeyCache, PublicKeyCache>();
        services.AddHttpClient<IAppManagerClient, AppManagerClient>()
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                var handler = new HttpClientHandler();
                var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
                var appManagerOptions = serviceProvider.GetRequiredService<IOptions<AppManagerOptions>>().Value;

                // REQ-NFR-004: refuse to dial AppManager over cleartext. Credentials and JWTs
                // ride this channel, so a non-https base URL is a hard failure (loopback http is
                // tolerated in Development only).
                AppManagerTransportSecurity.EnsureSecureBaseUrl(
                    appManagerOptions.BaseUrl, environment.IsDevelopment());

                // DEVELOPMENT ONLY: trust a self-signed AppManager TLS certificate when explicitly
                // opted in (AppManager:AllowUntrustedServerCertificate). The Development guard
                // means this can never relax certificate validation in Production, and it is scoped
                // to the AppManager typed client alone — never a global/default handler.
                if (AppManagerTransportSecurity.ShouldAcceptUntrustedCertificate(
                        environment.IsDevelopment(), appManagerOptions.AllowUntrustedServerCertificate))
                {
                    handler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }

                return handler;
            });

        // Auth mode switch and per-circuit session state (tokens stay server-side, REQ-FN-002).
        services.AddSingleton<ITechieDeskAuthModeProvider, TechieDeskAuthModeProvider>();
        services.AddScoped<SessionTokenStore>();
        services.AddScoped<ITokenRefresher, TokenRefresher>();

        // Identity, roles, capabilities, and server-side guard (REQ-FN-003/005/006/007).
        services.AddScoped<ITechieDeskUserContext, TechieDeskUserContext>();
        services.AddSingleton<ICapabilityService, CapabilityService>();
        services.AddScoped<IAuthGuard, AuthGuard>();
        services.AddScoped<IRouteGuard, RouteGuard>();

        services.AddScoped<TechieDeskAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(provider =>
            provider.GetRequiredService<TechieDeskAuthenticationStateProvider>());
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();

        return services;
    }

    /// <summary>
    /// Activates the auth pipeline: logs the active mode so operators can see at boot whether
    /// the instance runs offline (built-in Admin, no login) or against AppManager. Interactive
    /// route protection itself is enforced per-circuit by the registered
    /// <see cref="AuthenticationStateProvider"/> and <see cref="IRouteGuard"/> (BRD-20).
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application, for chaining.</returns>
    public static WebApplication UseTechieDeskAuth(this WebApplication app)
    {
        var modeProvider = app.Services.GetRequiredService<ITechieDeskAuthModeProvider>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TechieDesk.Auth");

        // REQ-NFR-004: fail fast at boot on a cleartext AppManager URL rather than on the first
        // login attempt, so a misconfigured deployment never starts serving.
        var appManagerOptions = app.Services.GetRequiredService<IOptions<AppManagerOptions>>().Value;
        AppManagerTransportSecurity.EnsureSecureBaseUrl(
            appManagerOptions.BaseUrl, app.Environment.IsDevelopment());

        if (modeProvider.IsAppManagerEnabled)
        {
            logger.LogInformation(
                "TechieDesk auth mode: AppManager — login required, roles enforced server-side");
        }
        else
        {
            logger.LogInformation(
                "TechieDesk auth mode: Offline single-user — no AppManager:BaseUrl configured, running as built-in Admin");
        }

        return app;
    }
}
