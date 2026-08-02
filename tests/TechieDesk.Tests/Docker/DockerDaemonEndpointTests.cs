using TechieDesk.Services;
using Xunit;

namespace TechieDesk.Tests.Docker;

/// <summary>
/// Guards the endpoint contract behind REQ-FN-040: the Docker daemon TechieDesk drives is a
/// setting, and the security posture of that setting is never assumed to be safe.
/// </summary>
public class DockerDaemonEndpointTests
{
    /// <summary>
    /// A unix socket endpoint parses as a local socket, stays uncleartext-warned (there is no
    /// network hop to warn about), and is handed to Docker.DotNet unchanged.
    /// </summary>
    [Fact]
    public void UnixSocketParsesAsLocalSocket()
    {
        var endpoint = DockerDaemonEndpoint.Parse("unix:///var/run/docker.sock");

        Assert.Equal(DockerDaemonEndpointKind.LocalSocket, endpoint.Kind);
        Assert.False(endpoint.UsesTls);
        Assert.Null(endpoint.SecurityWarningKey);
        Assert.Equal("unix:///var/run/docker.sock", endpoint.ClientUri.OriginalString);
    }

    /// <summary>
    /// A Windows named pipe is also a local socket, so the same screen serves both platforms.
    /// </summary>
    [Fact]
    public void NamedPipeParsesAsLocalSocket()
    {
        var endpoint = DockerDaemonEndpoint.Parse("npipe://./pipe/docker_engine");

        Assert.Equal(DockerDaemonEndpointKind.LocalSocket, endpoint.Kind);
        Assert.False(endpoint.UsesTls);
        Assert.Null(endpoint.SecurityWarningKey);
    }

    /// <summary>
    /// A LAN daemon reached over plain tcp:// parses as a network host and keeps Docker's
    /// conventional cleartext port when none is given.
    /// </summary>
    [Fact]
    public void PlainTcpParsesAsNetworkHost()
    {
        var endpoint = DockerDaemonEndpoint.Parse("tcp://qdrant-host.lan");

        Assert.Equal(DockerDaemonEndpointKind.NetworkHost, endpoint.Kind);
        Assert.Equal(DockerDaemonEndpoint.DefaultPlainPort, endpoint.Uri.Port);
        Assert.False(endpoint.UsesTls);
        Assert.Equal("tcp://qdrant-host.lan:2375", endpoint.Display);
    }

    /// <summary>
    /// A remote daemon reached over tcps:// parses as remote TLS, keeps Docker's conventional TLS
    /// port, and is handed to Docker.DotNet as https:// — the only scheme that library keeps
    /// encrypted. A tcp:// client URI here would silently downgrade the connection to cleartext.
    /// </summary>
    [Fact]
    public void RemoteTlsParsesAsEncryptedAndIsHandedToTheClientAsHttps()
    {
        var endpoint = DockerDaemonEndpoint.Parse("tcps://qdrant-host.lan");

        Assert.Equal(DockerDaemonEndpointKind.RemoteTls, endpoint.Kind);
        Assert.Equal(DockerDaemonEndpoint.DefaultTlsPort, endpoint.Uri.Port);
        Assert.True(endpoint.UsesTls);
        Assert.Equal("https", endpoint.ClientUri.Scheme);
        Assert.Equal("tcps://qdrant-host.lan:2376", endpoint.Display);
    }

    /// <summary>
    /// An explicit port always survives parsing, whatever the scheme's own default would be.
    /// </summary>
    [Theory]
    [InlineData("tcp://host:9999", 9999)]
    [InlineData("tcps://host:9999", 9999)]
    [InlineData("http://host:9999", 9999)]
    [InlineData("https://host:9999", 9999)]
    public void ExplicitPortIsPreserved(string raw, int expectedPort)
    {
        Assert.Equal(expectedPort, DockerDaemonEndpoint.Parse(raw).Uri.Port);
    }

    /// <summary>
    /// TLS verification is ON unless an operator explicitly turns it off. A Docker daemon is root
    /// on its host, so an unverified certificate must never be the default a user falls into.
    /// </summary>
    [Fact]
    public void TlsVerificationIsOnByDefault()
    {
        Assert.True(DockerDaemonEndpoint.Parse("tcps://host:2376").VerifyTls);
        Assert.True(DockerDaemonEndpoint.FromKind(DockerDaemonEndpointKind.RemoteTls, "host:2376").VerifyTls);
        Assert.True(DockerDaemonEndpoint.Local().VerifyTls);
        Assert.Null(DockerDaemonEndpoint.Parse("tcps://host:2376").SecurityWarningKey);
    }

    /// <summary>
    /// Turning verification off is allowed but is never silent — the endpoint carries a warning
    /// the screen must surface.
    /// </summary>
    [Fact]
    public void DisablingTlsVerificationRaisesWarning()
    {
        var endpoint = DockerDaemonEndpoint.Parse("tcps://host:2376", verifyTls: false);

        Assert.False(endpoint.VerifyTls);
        Assert.True(endpoint.HasSecurityWarning);
        Assert.Equal(DockerDaemonEndpoint.TlsVerificationDisabledWarningKey, endpoint.SecurityWarningKey);
    }

    /// <summary>
    /// A plain, unauthenticated TCP endpoint hands root on the target host to anyone on the
    /// network, so it always carries a warning — however it was spelled.
    /// </summary>
    [Theory]
    [InlineData("tcp://host:2375")]
    [InlineData("http://host:2375")]
    [InlineData("host:2375")]
    public void PlainTcpEndpointRaisesWarning(string raw)
    {
        var endpoint = DockerDaemonEndpoint.Parse(raw);

        Assert.Equal(DockerDaemonEndpointKind.NetworkHost, endpoint.Kind);
        Assert.True(endpoint.HasSecurityWarning);
        Assert.Equal(DockerDaemonEndpoint.PlainTcpWarningKey, endpoint.SecurityWarningKey);
    }

    /// <summary>
    /// A local socket is not a network exposure, so it must NOT be warned about — a warning that
    /// fires on the safe case trains operators to ignore it.
    /// </summary>
    [Fact]
    public void LocalSocketRaisesNoWarning()
    {
        Assert.False(DockerDaemonEndpoint.Local().HasSecurityWarning);
        Assert.False(DockerDaemonEndpoint.Parse("unix:///var/run/docker.sock").HasSecurityWarning);
    }

    /// <summary>
    /// Choosing "Remote (TCP + TLS)" in the UI wins over a pasted tcp:// address. Letting the
    /// pasted scheme win would downgrade the connection to cleartext behind the operator's back.
    /// </summary>
    [Fact]
    public void ChosenKindWinsOverPastedScheme()
    {
        var endpoint = DockerDaemonEndpoint.FromKind(
            DockerDaemonEndpointKind.RemoteTls, "tcp://host.lan:2376");

        Assert.Equal(DockerDaemonEndpointKind.RemoteTls, endpoint.Kind);
        Assert.True(endpoint.UsesTls);
        Assert.Equal("https", endpoint.ClientUri.Scheme);
    }

    /// <summary>
    /// Choosing the local socket resolves to this machine's daemon and ignores any address text.
    /// </summary>
    [Fact]
    public void LocalSocketKindIgnoresTypedAddress()
    {
        var endpoint = DockerDaemonEndpoint.FromKind(DockerDaemonEndpointKind.LocalSocket, "host.lan:2376");

        Assert.Equal(DockerDaemonEndpointKind.LocalSocket, endpoint.Kind);
        Assert.Equal(DockerDaemonEndpoint.Local().Display, endpoint.Display);
    }

    /// <summary>
    /// A blank endpoint means "this machine", not a parse failure — the local socket stays the
    /// zero-configuration default.
    /// </summary>
    [Fact]
    public void BlankEndpointResolvesToTheLocalSocket()
    {
        Assert.Equal(DockerDaemonEndpointKind.LocalSocket, DockerDaemonEndpoint.Parse(null).Kind);
        Assert.Equal(DockerDaemonEndpointKind.LocalSocket, DockerDaemonEndpoint.Parse("   ").Kind);
    }

    /// <summary>
    /// An endpoint TechieDesk cannot understand is rejected with a reason naming the offending
    /// text, rather than being quietly coerced into some other daemon.
    /// </summary>
    [Theory]
    [InlineData("ssh://host")]
    [InlineData("ftp://host:21")]
    public void UnsupportedSchemeIsRejectedWithAReason(string raw)
    {
        Assert.False(DockerDaemonEndpoint.TryParse(raw, verifyTls: true, out _, out var problem));
        Assert.NotNull(problem);
        Assert.Equal(DockerDaemonEndpoint.UnsupportedSchemeKey, problem!.MessageKey);
        Assert.Contains(raw, problem.Arguments);
    }

    /// <summary>
    /// A network endpoint kind with no address is refused rather than silently falling back to
    /// the local daemon.
    /// </summary>
    [Fact]
    public void NetworkKindWithoutAddressIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            DockerDaemonEndpoint.FromKind(DockerDaemonEndpointKind.RemoteTls, ""));
        Assert.Throws<ArgumentException>(() =>
            DockerDaemonEndpoint.FromKind(DockerDaemonEndpointKind.NetworkHost, null));
    }
}
