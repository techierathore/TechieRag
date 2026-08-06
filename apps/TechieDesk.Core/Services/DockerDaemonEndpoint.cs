using System.Globalization;

namespace TechieDesk.Services;

/// <summary>
/// How TechieDesk reaches the Docker daemon it administers (REQ-FN-040 / BRD-134).
/// </summary>
/// <remarks>
/// The daemon is never assumed to be local. The kind decides both the URI TechieDesk hands to
/// Docker.DotNet and the security posture it reports to the operator.
/// </remarks>
public enum DockerDaemonEndpointKind
{
    /// <summary>The daemon socket on this machine (<c>unix://</c> or <c>npipe://</c>).</summary>
    LocalSocket,

    /// <summary>A daemon on another machine over plain, unauthenticated <c>tcp://</c>.</summary>
    NetworkHost,

    /// <summary>A daemon on another machine over TCP protected by TLS.</summary>
    RemoteTls
}

/// <summary>
/// Why an operator-supplied Docker daemon endpoint could not be understood (REQ-UI-055 / BRD-91).
/// </summary>
/// <param name="MessageKey">Resource key for the explanation, resolved by whatever renders it.</param>
/// <param name="Arguments">
/// The values the key's placeholders take, in order. Every one of them is a machine value the
/// operator typed or a scheme name — never a translatable sentence.
/// </param>
/// <remarks>
/// REQ-UI-051's pattern: the service decides WHICH refusal applies, the presentation layer decides
/// what language it is written in. Returning a formatted English sentence from here is what put an
/// untranslated toast on a Hindi install.
/// </remarks>
public sealed record DockerEndpointProblem(string MessageKey, IReadOnlyList<string> Arguments)
{
    /// <summary>Renders the problem for a log line or an exception message.</summary>
    /// <returns>The key and its arguments, culture-invariant.</returns>
    /// <remarks>
    /// Deliberately NOT localized. Exception and log text is read by developers and by support, and
    /// a stack trace whose message changes with the reader's language cannot be searched for.
    /// </remarks>
    public override string ToString() =>
        Arguments.Count == 0 ? MessageKey : $"{MessageKey} [{string.Join("; ", Arguments)}]";
}

/// <summary>
/// A parsed and validated Docker daemon endpoint (REQ-FN-040).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Turns an operator-supplied endpoint string into the URI Docker.DotNet
/// needs plus the security facts the UI must surface — whether the channel is encrypted, whether
/// the server certificate is verified, and whether the configuration deserves a warning.</para>
/// <para><b>Security:</b> a Docker daemon endpoint is effectively root on the target host.
/// <see cref="VerifyTls"/> defaults to <see langword="true"/> everywhere, and any endpoint that
/// hands that power to the network without encryption carries a non-null
/// <see cref="SecurityWarningKey"/>.</para>
/// </remarks>
public sealed record DockerDaemonEndpoint
{
    /// <summary>Conventional Docker port for a TLS-protected daemon.</summary>
    public const int DefaultTlsPort = 2376;

    /// <summary>Conventional Docker port for an unencrypted daemon.</summary>
    public const int DefaultPlainPort = 2375;

    /// <summary>Unix domain socket path used by Docker on macOS and Linux.</summary>
    public const string UnixSocketEndpoint = "unix:///var/run/docker.sock";

    /// <summary>Named pipe used by Docker Desktop on Windows.</summary>
    public const string WindowsPipeEndpoint = "npipe://./pipe/docker_engine";

    /// <summary>
    /// Resource key for the warning shown whenever an unencrypted TCP daemon endpoint is configured.
    /// </summary>
    /// <remarks>REQ-UI-055: a KEY, so the warning is legible on a translated install.</remarks>
    public const string PlainTcpWarningKey = "QdrantDaemonPlainTcpWarning";

    /// <summary>
    /// Resource key for the warning shown when the operator has explicitly turned off TLS
    /// verification.
    /// </summary>
    public const string TlsVerificationDisabledWarningKey = "QdrantDaemonTlsVerificationDisabledWarning";

    /// <summary>Resource key for the message rejecting an endpoint that could not be parsed.</summary>
    public const string InvalidEndpointKey = "QdrantDaemonEndpointInvalid";

    /// <summary>Resource key for the message rejecting an endpoint whose scheme is not supported.</summary>
    public const string UnsupportedSchemeKey = "QdrantDaemonEndpointUnsupportedScheme";

    /// <summary>Resource key for the message rejecting a network endpoint that names no host.</summary>
    public const string MissingHostKey = "QdrantDaemonEndpointMissingHost";

    /// <summary>Resource key for the message rejecting a plain-TCP endpoint kind with no address.</summary>
    public const string MissingNetworkAddressKey = "QdrantDaemonEndpointMissingNetworkAddress";

    /// <summary>Resource key for the message rejecting a TLS endpoint kind with no address.</summary>
    public const string MissingTlsAddressKey = "QdrantDaemonEndpointMissingTlsAddress";

    /// <summary>Gets the kind of endpoint this is.</summary>
    public required DockerDaemonEndpointKind Kind { get; init; }

    /// <summary>
    /// Gets the canonical endpoint as the operator sees it — <c>unix://</c>, <c>npipe://</c>,
    /// <c>tcp://</c> or <c>tcps://</c>. This is what is shown in the UI and persisted.
    /// </summary>
    public required Uri Uri { get; init; }

    /// <summary>
    /// Gets the URI handed to Docker.DotNet. Identical to <see cref="Uri"/> except that a
    /// TLS endpoint is expressed as <c>https://</c>, which is the only scheme Docker.DotNet
    /// keeps encrypted.
    /// </summary>
    public required Uri ClientUri { get; init; }

    /// <summary>Gets a value indicating whether the channel to the daemon is encrypted.</summary>
    public required bool UsesTls { get; init; }

    /// <summary>
    /// Gets a value indicating whether the daemon's TLS certificate chain is verified.
    /// This is <see langword="true"/> unless an operator explicitly opts out.
    /// </summary>
    public bool VerifyTls { get; init; } = true;

    /// <summary>
    /// Gets the RESOURCE KEY for the security warning this configuration deserves, or
    /// <see langword="null"/> when the endpoint is safe (a local socket, or TLS with verification on).
    /// </summary>
    /// <remarks>
    /// REQ-UI-055 (BRD-91): this used to be the English sentence itself, which rendered untranslated
    /// on the Qdrant admin screen and in the toast raised when an unsafe endpoint is applied. A
    /// warning about handing root on a host to the network is the last thing that should be
    /// unreadable.
    /// </remarks>
    public string? SecurityWarningKey { get; init; }

    /// <summary>Gets a value indicating whether this endpoint carries a security warning.</summary>
    public bool HasSecurityWarning => !string.IsNullOrEmpty(SecurityWarningKey);

    /// <summary>Gets the endpoint rendered for display and persistence.</summary>
    /// <remarks>
    /// WIRE VOCABULARY. This is what is written to the daemon setting and what
    /// <see cref="Parse"/> reads back, so it is built from <see cref="Uri"/> and is identical in
    /// every culture. Nothing here is ever translated.
    /// </remarks>
    public string Display => Kind == DockerDaemonEndpointKind.LocalSocket
        ? Uri.OriginalString
        : $"{Uri.Scheme}://{Uri.Host}:{Uri.Port}";

    /// <summary>Gets the resource key for a short human label naming this endpoint's kind.</summary>
    public string KindLabelKey => KindLabelKeyFor(Kind);

    /// <summary>Gets the resource key for the short human label naming an endpoint kind.</summary>
    /// <param name="kind">The endpoint kind to name.</param>
    /// <returns>A key present in <c>AppStrings.resx</c>.</returns>
    /// <remarks>
    /// REQ-UI-055: the LABEL is localized; <see cref="DockerDaemonEndpointKind"/> itself is not, and
    /// nothing ever parses this label back into a kind. That trap is exactly how the daemon-kind
    /// Select once worked — the bound value WAS the English label and was parsed back to build the
    /// endpoint, so a translated install would have constructed a socket path from Devanagari.
    /// </remarks>
    public static string KindLabelKeyFor(DockerDaemonEndpointKind kind) => kind switch
    {
        DockerDaemonEndpointKind.NetworkHost => "QdrantEndpointKindNetworkHost",
        DockerDaemonEndpointKind.RemoteTls => "QdrantEndpointKindRemoteTls",
        _ => "QdrantEndpointKindLocalSocket"
    };

    /// <summary>
    /// Gets the local daemon endpoint for the current operating system — a named pipe on
    /// Windows, a unix domain socket everywhere else.
    /// </summary>
    /// <returns>The resolved local endpoint.</returns>
    public static DockerDaemonEndpoint Local()
    {
        var address = OperatingSystem.IsWindows() ? WindowsPipeEndpoint : UnixSocketEndpoint;
        var uri = new Uri(address);
        return new DockerDaemonEndpoint
        {
            Kind = DockerDaemonEndpointKind.LocalSocket,
            Uri = uri,
            ClientUri = uri,
            UsesTls = false,
            VerifyTls = true,
            SecurityWarningKey = null
        };
    }

    /// <summary>
    /// Parses an operator-supplied endpoint string, inferring the endpoint kind from its scheme.
    /// </summary>
    /// <param name="endpoint">
    /// The endpoint. <c>unix://…</c> and <c>npipe://…</c> are local sockets, <c>tcp://</c> and
    /// <c>http://</c> are plain network hosts, <c>tcps://</c> and <c>https://</c> are TLS. A bare
    /// <c>host</c> or <c>host:port</c> is treated as plain TCP. Blank resolves to
    /// <see cref="Local"/>.
    /// </param>
    /// <param name="verifyTls">
    /// Whether the daemon's certificate chain is verified. Defaults to <see langword="true"/>;
    /// passing <see langword="false"/> is an explicit, warned-about opt-out.
    /// </param>
    /// <returns>The parsed endpoint.</returns>
    /// <exception cref="ArgumentException">The endpoint could not be understood.</exception>
    /// <remarks>
    /// The exception message is culture-invariant on purpose (REQ-UI-055): exception text is read by
    /// developers and by support, and a message that changes with the reader's language cannot be
    /// searched for. A surface that has to SHOW the refusal calls <see cref="TryParse"/> and resolves
    /// <see cref="DockerEndpointProblem.MessageKey"/> instead.
    /// </remarks>
    public static DockerDaemonEndpoint Parse(string? endpoint, bool verifyTls = true)
    {
        if (!TryParse(endpoint, verifyTls, out var parsed, out var problem))
        {
            throw new ArgumentException(problem!.ToString(), nameof(endpoint));
        }

        return parsed;
    }

    /// <summary>
    /// Attempts to parse an operator-supplied endpoint string.
    /// </summary>
    /// <param name="endpoint">The endpoint text; see <see cref="Parse"/> for the accepted forms.</param>
    /// <param name="verifyTls">Whether the daemon's certificate chain is verified.</param>
    /// <param name="parsed">Receives the parsed endpoint on success.</param>
    /// <param name="problem">Receives the resource key and arguments describing the failure.</param>
    /// <returns><see langword="true"/> when the endpoint was understood.</returns>
    public static bool TryParse(
        string? endpoint,
        bool verifyTls,
        out DockerDaemonEndpoint parsed,
        out DockerEndpointProblem? problem)
    {
        problem = null;
        var text = endpoint?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            parsed = Local();
            return true;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            // A bare host or host:port is the commonest typo-free shorthand; Docker itself
            // treats it as TCP, and so do we — which means it warns, as plain TCP must.
            text = $"tcp://{text}";
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            parsed = Local();
            problem = new DockerEndpointProblem(InvalidEndpointKey, [endpoint ?? string.Empty]);
            return false;
        }

        switch (uri.Scheme.ToLowerInvariant())
        {
            case "unix":
            case "npipe":
                parsed = new DockerDaemonEndpoint
                {
                    Kind = DockerDaemonEndpointKind.LocalSocket,
                    Uri = uri,
                    ClientUri = uri,
                    UsesTls = false,
                    VerifyTls = true,
                    SecurityWarningKey = null
                };
                return true;

            case "tcp":
            case "http":
                if (!TryNormalizeHost(uri, DefaultPlainPort, "tcp", out var plainUri, out problem))
                {
                    parsed = Local();
                    return false;
                }

                parsed = new DockerDaemonEndpoint
                {
                    Kind = DockerDaemonEndpointKind.NetworkHost,
                    Uri = plainUri,
                    ClientUri = new UriBuilder(plainUri) { Scheme = "tcp" }.Uri,
                    UsesTls = false,
                    VerifyTls = true,
                    SecurityWarningKey = PlainTcpWarningKey
                };
                return true;

            case "tcps":
            case "https":
                if (!TryNormalizeHost(uri, DefaultTlsPort, "tcps", out var tlsUri, out problem))
                {
                    parsed = Local();
                    return false;
                }

                parsed = new DockerDaemonEndpoint
                {
                    Kind = DockerDaemonEndpointKind.RemoteTls,
                    Uri = tlsUri,
                    // Docker.DotNet only keeps a connection encrypted when the URI says https://.
                    ClientUri = new UriBuilder(tlsUri) { Scheme = "https" }.Uri,
                    UsesTls = true,
                    VerifyTls = verifyTls,
                    SecurityWarningKey = verifyTls ? null : TlsVerificationDisabledWarningKey
                };
                return true;

            default:
                parsed = Local();
                problem = new DockerEndpointProblem(
                    UnsupportedSchemeKey, [endpoint ?? string.Empty, uri.Scheme]);
                return false;
        }
    }

    /// <summary>
    /// Builds an endpoint from the kind the operator picked in the UI plus the address they typed.
    /// </summary>
    /// <param name="kind">The endpoint kind chosen in the UI.</param>
    /// <param name="address">
    /// The address for a network endpoint (<c>host</c>, <c>host:port</c> or a full URI). Ignored
    /// for <see cref="DockerDaemonEndpointKind.LocalSocket"/>.
    /// </param>
    /// <param name="verifyTls">Whether the daemon's certificate chain is verified.</param>
    /// <returns>The resolved endpoint.</returns>
    /// <exception cref="ArgumentException">The address could not be understood for that kind.</exception>
    public static DockerDaemonEndpoint FromKind(
        DockerDaemonEndpointKind kind,
        string? address,
        bool verifyTls = true)
    {
        if (!TryFromKind(kind, address, verifyTls, out var endpoint, out var problem))
        {
            throw new ArgumentException(problem!.ToString(), nameof(address));
        }

        return endpoint;
    }

    /// <summary>
    /// Builds an endpoint from the kind the operator picked plus the address they typed, reporting
    /// a refusal the caller can SHOW rather than throwing one it can only log.
    /// </summary>
    /// <param name="kind">The endpoint kind chosen in the UI.</param>
    /// <param name="address">The address for a network endpoint; ignored for a local socket.</param>
    /// <param name="verifyTls">Whether the daemon's certificate chain is verified.</param>
    /// <param name="endpoint">Receives the resolved endpoint on success.</param>
    /// <param name="problem">Receives the resource key and arguments describing the failure.</param>
    /// <returns><see langword="true"/> when the address was understood.</returns>
    /// <remarks>
    /// REQ-UI-055: the Qdrant admin screen used to render <c>ArgumentException.Message</c> straight
    /// into a toast, which is how an English sentence reached a Hindi window through a code path
    /// nobody thought of as UI text.
    /// </remarks>
    public static bool TryFromKind(
        DockerDaemonEndpointKind kind,
        string? address,
        bool verifyTls,
        out DockerDaemonEndpoint endpoint,
        out DockerEndpointProblem? problem)
    {
        problem = null;

        if (kind == DockerDaemonEndpointKind.LocalSocket)
        {
            endpoint = Local();
            return true;
        }

        var text = address?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            endpoint = Local();
            var isTls = kind == DockerDaemonEndpointKind.RemoteTls;
            var port = isTls ? DefaultTlsPort : DefaultPlainPort;
            problem = new DockerEndpointProblem(
                isTls ? MissingTlsAddressKey : MissingNetworkAddressKey,
                [port.ToString(CultureInfo.InvariantCulture)]);
            return false;
        }

        // The chosen kind wins over whatever scheme was typed: picking remote TLS and pasting a
        // tcp:// address must not silently downgrade the connection to cleartext.
        var separator = text.IndexOf("://", StringComparison.Ordinal);
        var hostPart = separator >= 0 ? text[(separator + 3)..] : text;
        var scheme = kind == DockerDaemonEndpointKind.RemoteTls ? "tcps" : "tcp";

        return TryParse($"{scheme}://{hostPart}", verifyTls, out endpoint, out problem);
    }

    private static bool TryNormalizeHost(
        Uri uri,
        int defaultPort,
        string scheme,
        out Uri normalized,
        out DockerEndpointProblem? problem)
    {
        problem = null;
        normalized = uri;

        if (string.IsNullOrEmpty(uri.Host))
        {
            normalized = Local().Uri;
            problem = new DockerEndpointProblem(
                MissingHostKey,
                [uri.ToString(), scheme, defaultPort.ToString(CultureInfo.InvariantCulture)]);
            return false;
        }

        // tcp:// and tcps:// are unregistered schemes, so an unspecified port surfaces as the
        // "default" (-1); http/https surface as 80/443. In every one of those cases the operator
        // did not name a port, so Docker's conventional port for the channel applies.
        var port = uri.IsDefaultPort ? defaultPort : uri.Port;
        normalized = new UriBuilder(scheme, uri.Host, port).Uri;
        return true;
    }
}
