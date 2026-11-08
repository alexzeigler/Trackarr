using System.Net;
using System.Security.Claims;
using Trackarr.Services.Settings;

namespace Trackarr.Services.Auth;

/// <summary>Where a request came from, as far as local network access is concerned.</summary>
public enum RequestOrigin
{
    /// <summary>No IP address to go on (not a TCP connection).</summary>
    NoAddress,

    /// <summary>From an address on one of the local networks.</summary>
    Local,

    /// <summary>From an address outside the local networks.</summary>
    NotLocal,

    /// <summary>Through a proxy Trackarr can't see past, so the address is the proxy's and the visitor is unknown.</summary>
    UntrustedProxy
}

/// <summary>
///     Settings &gt; Security &gt; "Don't ask for the login on local networks". Off by default.
///     <para>
///         A request only counts as local when its address is on a local network <em>and</em> nothing says a proxy sits
///         in front that Trackarr can't see past. Behind a proxy whose forwarded headers aren't trusted, every visitor
///         arrives from the proxy's private address, so they would all look local. That is how the same option in
///         Sonarr was bypassed (CVE-2026-30975). Here, any proxy header that the forwarded-headers step didn't handle
///         means "not local", and the login is asked for.
///     </para>
/// </summary>
public sealed class LocalAccess(SettingsService settings)
{
    public const string Category = "Security";
    public const string EnabledKey = "LocalAccess";
    public const string NetworksKey = "LocalNetworks";

    /// <summary>Authentication type of the stand-in principal a local visitor gets.</summary>
    public const string AuthenticationType = "LocalNetwork";

    public const string DisplayName = "Local network";
    public const string AddressClaim = "trackarr:address";
    public const string OriginClaim = "trackarr:origin";

    public const string ForwardedForHeader = "X-Forwarded-For";

    /// <summary>Headers that proxies and CDNs use to pass on the visitor's address.</summary>
    public static readonly IReadOnlyList<string> ProxyHeaders =
    [
        ForwardedForHeader, "Forwarded", "X-Real-IP", "CF-Connecting-IP", "True-Client-IP", "X-Client-IP",
        "X-Cluster-Client-IP", "Fastly-Client-IP"
    ];

    private ParsedNetworks? _parsed;

    public bool Enabled => settings.GetBool(Category, EnabledKey);

    /// <summary>The local networks from settings, parsed once per change.</summary>
    public IReadOnlyList<IPNetwork> Networks
    {
        get
        {
            var value = settings.GetString(Category, NetworksKey);
            var parsed = _parsed;
            if (parsed is null || parsed.Value != value)
                _parsed = parsed = new ParsedNetworks(value, ParseNetworks(value, out _));
            return parsed.Networks;
        }
    }

    /// <summary>
    ///     Same format and default as Trusted Proxies: IPs or CIDR ranges, and empty means loopback plus the private
    ///     ranges (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, fc00::/7, fe80::/10). A value with only invalid entries
    ///     gives no networks, so nothing counts as local.
    /// </summary>
    public static List<IPNetwork> ParseNetworks(string? value, out List<string> invalid)
    {
        return HostingOptions.ParseTrustedProxies(value, out invalid);
    }

    /// <summary>
    ///     True when X-Forwarded-For came in and the forwarded-headers step used all of it, which it only does for a
    ///     trusted proxy. <paramref name="hadForwardedFor" /> is whether the header was there before that step.
    /// </summary>
    public static bool ProxyResolved(bool hadForwardedFor, Func<string, bool> hasHeader)
    {
        return hadForwardedFor && !hasHeader(ForwardedForHeader);
    }

    /// <summary>
    ///     Classifies a request. <paramref name="address" /> is the client address after the forwarded-headers step,
    ///     and <paramref name="hasHeader" /> tells whether a request header is present after it.
    /// </summary>
    public static RequestOrigin Classify(IPAddress? address, bool proxyResolved, Func<string, bool> hasHeader,
        IReadOnlyList<IPNetwork> networks)
    {
        if (address is null) return RequestOrigin.NoAddress;

        // X-Forwarded-For left over: no trusted proxy handled it, or it lists more hops than Trackarr trusts.
        if (hasHeader(ForwardedForHeader)) return RequestOrigin.UntrustedProxy;

        // Another proxy header, and no X-Forwarded-For was handled: the address is the proxy's, not the visitor's.
        if (!proxyResolved && ProxyHeaders.Any(hasHeader)) return RequestOrigin.UntrustedProxy;

        return IsInNetworks(address, networks) ? RequestOrigin.Local : RequestOrigin.NotLocal;
    }

    public static bool IsInNetworks(IPAddress address, IReadOnlyList<IPNetwork> networks)
    {
        var normalized = Normalize(address);
        return networks.Any(n => n.Contains(normalized));
    }

    /// <summary>
    ///     For an open page, checked again every minute: still allowed while the option is on and the address the page
    ///     connected from is still on a local network.
    /// </summary>
    public bool StillAllows(ClaimsPrincipal principal)
    {
        return Enabled && IsLocalPrincipal(principal) &&
               IPAddress.TryParse(principal.FindFirstValue(AddressClaim), out var address) &&
               IsInNetworks(address, Networks);
    }

    /// <summary>The stand-in login for a visitor on a local network.</summary>
    public static ClaimsPrincipal CreatePrincipal(IPAddress address)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, DisplayName),
            new Claim(AddressClaim, Normalize(address).ToString()),
            new Claim(OriginClaim, nameof(RequestOrigin.Local))
        ], AuthenticationType));
    }

    /// <summary>
    ///     Where the request came from, as an extra identity on the principal so Settings &gt; Security can show it. It
    ///     has no authentication type, so it never counts as signed in.
    /// </summary>
    public static ClaimsIdentity CreateOriginIdentity(IPAddress? address, RequestOrigin origin)
    {
        List<Claim> claims = [new(OriginClaim, origin.ToString())];
        if (address is not null) claims.Add(new Claim(AddressClaim, Normalize(address).ToString()));
        return new ClaimsIdentity(claims);
    }

    public static bool IsLocalPrincipal(ClaimsPrincipal? principal)
    {
        return principal?.Identity is { IsAuthenticated: true, AuthenticationType: AuthenticationType };
    }

    /// <summary>The address and origin recorded on a principal, if any.</summary>
    public static (IPAddress? Address, RequestOrigin? Origin) ReadOrigin(ClaimsPrincipal? principal)
    {
        if (principal is null) return (null, null);

        var address = IPAddress.TryParse(principal.FindFirstValue(AddressClaim), out var parsed) ? parsed : null;
        var origin = Enum.TryParse<RequestOrigin>(principal.FindFirstValue(OriginClaim), out var value)
            ? value
            : (RequestOrigin?)null;
        return (address, origin);
    }

    // Dual-stack sockets report IPv4 clients as ::ffff:192.168.1.5, which no IPv4 range contains.
    private static IPAddress Normalize(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private sealed record ParsedNetworks(string Value, IReadOnlyList<IPNetwork> Networks);
}
