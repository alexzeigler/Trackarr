using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trackarr.Core.Auth;
using Trackarr.Services.Auth;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;
using IPNetwork = System.Net.IPNetwork;

namespace Trackarr.Tests;

public sealed class LocalAccessTests : IDisposable
{
    private static readonly IReadOnlyList<IPNetwork> PrivateNetworks = LocalAccess.ParseNetworks("", out _);

    private readonly SqliteFileFactory _factory = new();

    public void Dispose()
    {
        _factory.Dispose();
    }

    #region Classify

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("192.168.1.20")]
    [InlineData("10.1.2.3")]
    [InlineData("172.18.0.5")]
    [InlineData("fd00::5")]
    [InlineData("::ffff:192.168.1.20")] // an IPv4 visitor on a dual-stack socket
    public void Classify_PrivateAddressWithoutProxyHeaders_IsLocal(string address)
    {
        Assert.Equal(RequestOrigin.Local, Classify(address));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("100.64.0.1")] // carrier-grade NAT (and Tailscale) is shared address space, not private
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:8.8.8.8")]
    public void Classify_PublicAddress_IsNotLocal(string address)
    {
        Assert.Equal(RequestOrigin.NotLocal, Classify(address));
    }

    [Fact]
    public void Classify_NoAddress_IsNoAddress()
    {
        Assert.Equal(RequestOrigin.NoAddress, Classify(null));
    }

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("x-forwarded-for")]
    [InlineData("Forwarded")]
    [InlineData("X-Real-IP")]
    [InlineData("CF-Connecting-IP")]
    [InlineData("True-Client-IP")]
    public void Classify_ProxyHeaderNothingHandled_IsUntrustedProxy(string header)
    {
        // The address is a private one, but it's the proxy's.
        Assert.Equal(RequestOrigin.UntrustedProxy, Classify("172.18.0.2", false, header));
    }

    [Fact]
    public void Classify_OtherProxyHeaders_AreFineOnceForwardedForWasResolved()
    {
        Assert.Equal(RequestOrigin.Local, Classify("192.168.1.20", true, "X-Real-IP"));
    }

    [Fact]
    public void Classify_LeftoverForwardedFor_IsUntrustedProxyEvenWhenResolved()
    {
        Assert.Equal(RequestOrigin.UntrustedProxy, Classify("192.168.1.20", true, "X-Forwarded-For"));
    }

    [Fact]
    public void Classify_UsesTheConfiguredNetworks()
    {
        var lan = LocalAccess.ParseNetworks("192.168.1.0/24", out var invalid);

        Assert.Empty(invalid);
        Assert.Equal(RequestOrigin.Local, LocalAccess.Classify(IPAddress.Parse("192.168.1.20"), false, _ => false, lan));
        Assert.Equal(RequestOrigin.NotLocal, LocalAccess.Classify(IPAddress.Parse("172.18.0.5"), false, _ => false, lan));
    }

    [Fact]
    public void Classify_OnlyInvalidNetworks_NothingIsLocal()
    {
        var none = LocalAccess.ParseNetworks("nope", out var invalid);

        Assert.Equal(["nope"], invalid);
        Assert.Equal(RequestOrigin.NotLocal, LocalAccess.Classify(IPAddress.Loopback, false, _ => false, none));
    }

    #endregion

    #region Behind a proxy (the real ForwardedHeadersMiddleware, set up as in Program.cs)

    [Fact]
    public async Task TrustedProxy_ForwardingALocalVisitor_IsLocal()
    {
        Assert.Equal(RequestOrigin.Local,
            await ThroughAsync("172.18.0.2", true, ("X-Forwarded-For", "192.168.1.20")));
    }

    [Fact]
    public async Task TrustedProxy_ForwardingAnInternetVisitor_IsNotLocal()
    {
        Assert.Equal(RequestOrigin.NotLocal,
            await ThroughAsync("172.18.0.2", true, ("X-Forwarded-For", "203.0.113.9")));
    }

    [Fact]
    public async Task TrustedProxy_NginxStyleHeaders_IsLocal()
    {
        Assert.Equal(RequestOrigin.Local,
            await ThroughAsync("172.18.0.2", true, ("X-Forwarded-For", "192.168.1.20"),
                ("X-Real-IP", "192.168.1.20")));
    }

    [Fact]
    public async Task TrustedProxy_VisitorSpoofingForwardedFor_IsNotLocal()
    {
        // The visitor sent "X-Forwarded-For: 127.0.0.1" and the proxy appended their real address.
        Assert.Equal(RequestOrigin.UntrustedProxy,
            await ThroughAsync("172.18.0.2", true, ("X-Forwarded-For", "127.0.0.1, 203.0.113.9")));
    }

    [Fact]
    public async Task TrustedProxy_SendingOnlyXRealIp_IsUntrustedProxy()
    {
        Assert.Equal(RequestOrigin.UntrustedProxy,
            await ThroughAsync("172.18.0.2", true, ("X-Real-IP", "192.168.1.20")));
    }

    [Fact]
    public async Task InternetVisitorClaimingToBeLocal_IsUntrustedProxy()
    {
        // Straight to Trackarr's port from the internet: the forwarded-headers step doesn't trust the sender.
        Assert.Equal(RequestOrigin.UntrustedProxy,
            await ThroughAsync("203.0.113.9", true, ("X-Forwarded-For", "127.0.0.1")));
    }

    [Fact]
    public async Task ProxyTrustOff_VisitorThroughTheProxy_IsUntrustedProxy()
    {
        // The CVE-2026-30975 setup: a proxy on the Docker network and "Trust forwarded headers" off. The address is
        // the proxy's private one, so without the header check every visitor would look local.
        Assert.Equal(RequestOrigin.UntrustedProxy,
            await ThroughAsync("172.18.0.2", false, ("X-Forwarded-For", "203.0.113.9")));
        Assert.Equal(RequestOrigin.UntrustedProxy,
            await ThroughAsync("172.18.0.2", false, ("X-Real-IP", "203.0.113.9")));
    }

    [Fact]
    public async Task ProxyTrustOff_DirectLocalVisitor_IsLocal()
    {
        Assert.Equal(RequestOrigin.Local, await ThroughAsync("192.168.1.20", false));
    }

    #endregion

    #region Principals and settings

    [Fact]
    public void LocalPrincipal_IsSignedInAndRecognized()
    {
        var principal = LocalAccess.CreatePrincipal(IPAddress.Parse("::ffff:192.168.1.20"));

        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.True(LocalAccess.IsLocalPrincipal(principal));
        Assert.Equal(LocalAccess.DisplayName, principal.Identity?.Name);
        Assert.Equal((IPAddress.Parse("192.168.1.20"), RequestOrigin.Local), LocalAccess.ReadOrigin(principal));
    }

    [Fact]
    public void CookieAndAnonymousPrincipals_AreNotLocal()
    {
        var user = new User { Id = 1, Username = "admin", PasswordHash = "x" };

        Assert.False(LocalAccess.IsLocalPrincipal(UserService.CreatePrincipal(user, "Cookies")));
        Assert.False(LocalAccess.IsLocalPrincipal(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.False(LocalAccess.IsLocalPrincipal(null));
    }

    [Fact]
    public void OriginIdentity_NeverCountsAsSignedIn()
    {
        var address = IPAddress.Parse("192.168.1.20");
        var principal = new ClaimsPrincipal(
            [new ClaimsIdentity(), LocalAccess.CreateOriginIdentity(address, RequestOrigin.Local)]);

        Assert.DoesNotContain(principal.Identities, i => i.IsAuthenticated);
        Assert.False(LocalAccess.IsLocalPrincipal(principal));
        Assert.Equal((address, RequestOrigin.Local), LocalAccess.ReadOrigin(principal));
    }

    [Fact]
    public async Task StillAllows_FollowsTheSettings()
    {
        var settings = new SettingsService(_factory, NullLogger<SettingsService>.Instance, new DataChangeBus());
        await settings.InitializeAsync();
        var access = new LocalAccess(settings);
        var principal = LocalAccess.CreatePrincipal(IPAddress.Parse("192.168.1.20"));

        Assert.False(access.Enabled); // off by default
        Assert.False(access.StillAllows(principal));

        await settings.SaveCategoryAsync(LocalAccess.Category,
            new Dictionary<string, string> { [LocalAccess.EnabledKey] = "True" });
        Assert.True(access.StillAllows(principal));

        await settings.SaveCategoryAsync(LocalAccess.Category,
            new Dictionary<string, string> { [LocalAccess.NetworksKey] = "10.0.0.0/8" });
        Assert.False(access.StillAllows(principal));
    }

    #endregion

    private static RequestOrigin Classify(string? address, bool proxyResolved = false, params string[] headers)
    {
        return LocalAccess.Classify(address is null ? null : IPAddress.Parse(address), proxyResolved,
            h => headers.Contains(h, StringComparer.OrdinalIgnoreCase), PrivateNetworks);
    }

    // The same steps as Program.cs: note whether X-Forwarded-For came in, run the forwarded-headers step when
    // "Trust forwarded headers" is on (trusting the default private networks), then classify.
    private static async Task<RequestOrigin> ThroughAsync(string remote, bool trustProxies,
        params (string Name, string Value)[] headers)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        foreach (var (name, value) in headers)
            context.Request.Headers[name] = value;

        var hadForwardedFor = false;
        if (trustProxies)
        {
            hadForwardedFor = context.Request.Headers.ContainsKey(LocalAccess.ForwardedForHeader);

            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto |
                                   ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedPrefix
            };
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var network in HostingOptions.ParseTrustedProxies("", out _))
                options.KnownIPNetworks.Add(network);

            await new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance,
                Options.Create(options)).Invoke(context);
        }

        var after = context.Request.Headers;
        return LocalAccess.Classify(context.Connection.RemoteIpAddress,
            LocalAccess.ProxyResolved(hadForwardedFor, after.ContainsKey), after.ContainsKey, PrivateNetworks);
    }
}
