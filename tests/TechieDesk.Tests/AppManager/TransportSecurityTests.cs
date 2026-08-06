using TechieDesk.Services.AppManager;
using Xunit;

namespace TechieDesk.Tests.AppManager;

/// <summary>
/// REQ-NFR-004 / BRD-95: outbound AppManager traffic must be TLS-protected, and the
/// self-signed-certificate escape hatch must be unreachable outside Development.
/// </summary>
public sealed class TransportSecurityTests
{
    /// <summary>
    /// An https base URL is accepted in every environment — this is the supported deployment.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HttpsBaseUrlIsAccepted(bool isDevelopment)
    {
        AppManagerTransportSecurity.EnsureSecureBaseUrl("https://appmanager.example.com", isDevelopment);
    }

    /// <summary>
    /// An empty base URL selects offline single-user mode (BRD-54); no call is ever made, so
    /// there is nothing to secure and validation must not block startup.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyBaseUrlIsOfflineModeAndIsAccepted(string? baseUrl)
    {
        AppManagerTransportSecurity.EnsureSecureBaseUrl(baseUrl, isDevelopment: false);
    }

    /// <summary>
    /// A cleartext AppManager URL is rejected outside Development — API credentials and JWT
    /// tokens must never traverse plain http, even on a private network.
    /// </summary>
    [Theory]
    [InlineData("http://appmanager.example.com")]
    [InlineData("http://192.168.1.14:5101/")]
    [InlineData("http://localhost:5101")]
    public void CleartextBaseUrlIsRejectedOutsideDevelopment(string baseUrl)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => AppManagerTransportSecurity.EnsureSecureBaseUrl(baseUrl, isDevelopment: false));

        Assert.Contains("https", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Even in Development, cleartext is only tolerated for a loopback host — a developer may
    /// run AppManager locally without a certificate, but never reach a remote host in the clear.
    /// </summary>
    [Fact]
    public void CleartextRemoteHostIsRejectedEvenInDevelopment()
    {
        Assert.Throws<InvalidOperationException>(
            () => AppManagerTransportSecurity.EnsureSecureBaseUrl(
                "http://192.168.1.14:5101/", isDevelopment: true));
    }

    /// <summary>
    /// Loopback http is the one permitted cleartext case, and only in Development.
    /// </summary>
    [Theory]
    [InlineData("http://localhost:5101")]
    [InlineData("http://127.0.0.1:5101")]
    public void LoopbackCleartextIsAllowedInDevelopmentOnly(string baseUrl)
    {
        AppManagerTransportSecurity.EnsureSecureBaseUrl(baseUrl, isDevelopment: true);

        Assert.Throws<InvalidOperationException>(
            () => AppManagerTransportSecurity.EnsureSecureBaseUrl(baseUrl, isDevelopment: false));
    }

    /// <summary>
    /// A malformed base URL fails loudly at startup rather than at the first login attempt.
    /// </summary>
    [Fact]
    public void RelativeOrMalformedBaseUrlIsRejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => AppManagerTransportSecurity.EnsureSecureBaseUrl("appmanager.example.com", isDevelopment: false));
    }

    /// <summary>
    /// The untrusted-certificate opt-in is honoured only when the flag is set AND the host is
    /// Development. Configuration alone can never disable certificate validation in Production.
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void UntrustedCertificateRequiresBothDevelopmentAndOptIn(
        bool isDevelopment, bool allowUntrusted, bool expected)
    {
        Assert.Equal(
            expected,
            AppManagerTransportSecurity.ShouldAcceptUntrustedCertificate(isDevelopment, allowUntrusted));
    }

    /// <summary>
    /// Explicit regression guard for the highest-risk combination: a Production deployment that
    /// still carries <c>AllowUntrustedServerCertificate: true</c> in its configuration must
    /// nonetheless validate certificates.
    /// </summary>
    [Fact]
    public void ProductionNeverAcceptsUntrustedCertificateEvenWhenFlagIsSet()
    {
        Assert.False(AppManagerTransportSecurity.ShouldAcceptUntrustedCertificate(
            isDevelopment: false, allowUntrustedServerCertificate: true));
    }
}
