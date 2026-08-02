namespace TechieDesk.Services.AppManager;

/// <summary>
/// Transport-security policy for outbound AppManager traffic (REQ-NFR-004 / BRD-95).
/// </summary>
/// <remarks>
/// <para>
/// Two decisions live here so that both are pure, centrally reviewable, and unit-testable
/// rather than being scattered through DI wiring:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="EnsureSecureBaseUrl"/> — AppManager carries credentials and JWTs, so the
/// channel must be TLS. Outside Development a non-<c>https</c> base URL is a hard startup
/// failure rather than a silent downgrade to cleartext.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="ShouldAcceptUntrustedCertificate"/> — the dev-only self-signed-certificate
/// escape hatch, which can never be enabled outside Development regardless of configuration.
/// </description>
/// </item>
/// </list>
/// </remarks>
public static class AppManagerTransportSecurity
{
    /// <summary>
    /// Validates that a configured AppManager base URL uses TLS.
    /// </summary>
    /// <remarks>
    /// An empty base URL is valid — it selects offline single-user mode (BRD-54), in which no
    /// AppManager call is ever made. In Development a loopback <c>http</c> host is tolerated so
    /// a developer can run AppManager locally without a certificate; every other non-TLS URL
    /// throws, in Development and Production alike.
    /// </remarks>
    /// <param name="baseUrl">The configured <c>AppManager:BaseUrl</c> value.</param>
    /// <param name="isDevelopment"><see langword="true"/> when the host environment is Development.</param>
    /// <exception cref="InvalidOperationException">
    /// The base URL is not an absolute URI, or it is cleartext where TLS is required.
    /// </exception>
    public static void EnsureSecureBaseUrl(string? baseUrl, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // Offline single-user mode — nothing is dialled, nothing to secure.
            return;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"AppManager:BaseUrl ('{baseUrl}') is not an absolute URI.");
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return;
        }

        if (isDevelopment && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        {
            // Local developer AppManager instance; never reachable off-box.
            return;
        }

        throw new InvalidOperationException(
            $"AppManager:BaseUrl must use https — '{uri.Scheme}' would send API credentials and " +
            "JWT tokens in cleartext (REQ-NFR-004). Only a loopback http host is permitted, and " +
            "only in the Development environment.");
    }

    /// <summary>
    /// Determines whether the AppManager HTTP client may accept an untrusted / self-signed
    /// TLS server certificate.
    /// </summary>
    /// <remarks>
    /// The Development check is applied here — and only here — so that no configuration value
    /// can relax certificate validation in Staging or Production.
    /// </remarks>
    /// <param name="isDevelopment"><see langword="true"/> when the host environment is Development.</param>
    /// <param name="allowUntrustedServerCertificate">
    /// The <c>AppManager:AllowUntrustedServerCertificate</c> opt-in flag.
    /// </param>
    /// <returns>
    /// <see langword="true"/> only when the host is Development <em>and</em> the flag is set.
    /// </returns>
    public static bool ShouldAcceptUntrustedCertificate(
        bool isDevelopment,
        bool allowUntrustedServerCertificate)
        => isDevelopment && allowUntrustedServerCertificate;
}
