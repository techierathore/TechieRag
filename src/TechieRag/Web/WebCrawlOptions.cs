using System.Net;

namespace TechieRag.Web;

/// <summary>
/// Bounds on a crawl (REQ-RAG-017 / BRD-61).
/// </summary>
/// <remarks>
/// Every default here is the conservative one. A crawler is the one ingestion path that can generate
/// unbounded work and unbounded outbound traffic from a single click, so the defaults are the ones a
/// user would pick if they had thought about it, and widening them is an explicit act.
/// </remarks>
public sealed class WebCrawlOptions
{
    /// <summary>Gets or sets how many links deep to follow. 0 fetches only the seed page.</summary>
    public int MaxDepth { get; set; } = 1;

    /// <summary>Gets or sets the hard cap on pages fetched, including the seed.</summary>
    public int MaxPages { get; set; } = 25;

    /// <summary>Gets or sets whether the crawl stays on the seed URL's host.</summary>
    /// <remarks>
    /// True by design. A crawl that follows off-site links is not "ingest this site" — it is an
    /// open-ended walk of the web that happens to start at the seed, and one link to a large site
    /// would exhaust the page budget on content the user never asked for.
    /// </remarks>
    public bool SameHostOnly { get; set; } = true;

    /// <summary>Gets or sets the pause between requests.</summary>
    /// <remarks>Politeness: a crawler with no delay is indistinguishable from a small denial of service.</remarks>
    public TimeSpan RequestDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets or sets whether URLs resolving to private, loopback or link-local addresses are refused.
    /// </summary>
    /// <remarks>
    /// True by default, and it is a security control rather than a nicety. The crawler follows links
    /// it did not choose, so a page on the open internet can point it at <c>http://localhost:8080</c>
    /// or <c>http://169.254.169.254</c> and use this application as a proxy into the machine's own
    /// network — server-side request forgery. Refusing private targets by default means an operator
    /// crawling a public site cannot be redirected into their own LAN. It is settable because
    /// crawling an intranet is a legitimate thing to want, and then the operator is choosing it.
    /// </remarks>
    public bool BlockPrivateNetworkTargets { get; set; } = true;

    /// <summary>Determines whether a host is a private-network target this crawl should refuse.</summary>
    /// <param name="host">The host component of a candidate URL.</param>
    /// <returns>True when the host is loopback, private, or link-local.</returns>
    /// <remarks>
    /// <para>Literal-address and well-known-name checks only; no DNS resolution is performed. This is
    /// the CHEAP half of the guard, used to filter candidate links during a crawl where resolving
    /// every anchor on every page would add a DNS round trip per link for no additional safety.</para>
    /// <para><b>It is not the enforcement point, and must not be treated as one.</b> A name like
    /// <c>127.0.0.1.nip.io</c> is an ordinary public domain as far as this method can tell, and it
    /// resolves to loopback. The check that actually holds is in <see cref="HttpWebContentFetcher"/>,
    /// which refuses the connection on the RESOLVED address — see
    /// <see cref="HttpWebContentFetcher.CreateGuardedHandler"/>. Anything that opens a socket must go
    /// through that; this method only decides what is worth putting in a crawl queue.</para>
    /// </remarks>
    public static bool IsPrivateNetworkHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host.Trim('[', ']'), out var address)
               && IsPrivateNetworkAddress(address);
    }

    /// <summary>Determines whether a resolved IP address is one ingestion must never reach.</summary>
    /// <param name="address">An address a candidate host resolved to.</param>
    /// <returns>True when the address is loopback, private, link-local or otherwise not routable.</returns>
    /// <remarks>
    /// Split out from <see cref="IsPrivateNetworkHost"/> so the same ranges decide both the cheap
    /// textual filter and the connect-time check. Two lists would drift, and the one that drifted
    /// would be the one nobody was reading when the guard was bypassed.
    /// </remarks>
    public static bool IsPrivateNetworkAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal)
        {
            return true;
        }

        // An IPv4 address carried inside an IPv6 one is still that IPv4 address. Skipping the
        // unwrap is how ::ffff:127.0.0.1 walks straight through a v4-only range check.
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPrivateNetworkAddress(address.MapToIPv4());
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        return bytes[0] switch
        {
            10 => true,                                        // 10.0.0.0/8
            127 => true,                                       // 127.0.0.0/8
            169 when bytes[1] == 254 => true,                  // 169.254.0.0/16 — cloud metadata
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true, // 172.16.0.0/12
            192 when bytes[1] == 168 => true,                  // 192.168.0.0/16
            100 when bytes[1] >= 64 && bytes[1] <= 127 => true, // 100.64.0.0/10 — carrier-grade NAT
            0 => true,                                         // 0.0.0.0/8
            _ => false,
        };
    }
}
