using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Data;
using Trackarr.Integrations.Media.Arr;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

public class NetworkSettingsTests
{
    #region HostingOptions

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData("trackarr", "/trackarr")]
    [InlineData("/trackarr/", "/trackarr")]
    [InlineData(" /a/b ", "/a/b")]
    public void UrlBase_IsNormalized(string? input, string expected)
    {
        Assert.Equal(expected, HostingOptions.NormalizeUrlBase(input));
    }

    [Fact]
    public void TrustedProxies_EmptyMeansLoopbackAndPrivateNetworks()
    {
        var networks = HostingOptions.ParseTrustedProxies("", out var invalid);

        Assert.Empty(invalid);
        Assert.Contains(networks, n => n.Contains(IPAddress.Parse("172.18.0.2"))); // Docker bridge
        Assert.Contains(networks, n => n.Contains(IPAddress.Parse("192.168.1.10")));
        Assert.Contains(networks, n => n.Contains(IPAddress.Loopback));
        Assert.DoesNotContain(networks, n => n.Contains(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void TrustedProxies_ParsesIpsAndRanges_AndReportsInvalid()
    {
        var networks = HostingOptions.ParseTrustedProxies("10.0.0.5, 172.18.0.0/16 nope", out var invalid);

        Assert.Equal(2, networks.Count);
        Assert.Contains(networks, n => n.Contains(IPAddress.Parse("10.0.0.5")));
        Assert.DoesNotContain(networks, n => n.Contains(IPAddress.Parse("10.0.0.6")));
        Assert.Equal(["nope"], invalid);
    }

    [Fact]
    public void Load_UsesDefaults_WhenTheDatabaseIsNew()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackarr-host-{Guid.NewGuid():N}.db");
        try
        {
            var options = HostingOptions.Load($"Data Source={path}", null);

            Assert.Equal(HostingOptions.DefaultPort, options.Port);
            Assert.Equal("http://*:8585", options.ListenUrls);
            Assert.Equal("http://localhost:8585/health", options.LocalHealthUrl);
            Assert.Equal("/", options.BaseHref);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Load_ReadsSavedNetworkSettings_AndUrlsOverrideWins()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackarr-host-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;
            await using (var db = new AppDbContext(dbOptions)) await db.Database.MigrateAsync();

            var factory = new OptionsFactory(dbOptions);
            var settings = new SettingsService(factory, NullLogger<SettingsService>.Instance, new DataChangeBus());
            await settings.InitializeAsync();
            await settings.SaveCategoryAsync("Network", new Dictionary<string, string>
            {
                ["Port"] = "9000", ["BindAddress"] = "::1", ["UrlBase"] = "trackarr", ["ReverseProxy"] = "true"
            });

            var options = HostingOptions.Load(connectionString, null);
            Assert.Equal("http://[::1]:9000", options.ListenUrls); // IPv6 gets brackets
            Assert.Equal("/trackarr", options.UrlBase);
            Assert.Equal("/trackarr/", options.BaseHref);
            Assert.True(options.ReverseProxy);

            var overridden = HostingOptions.Load(connectionString, "http://+:8080");
            Assert.Equal("http://+:8080", overridden.ListenUrls);
            Assert.Equal("http://localhost:8080/health", overridden.LocalHealthUrl);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private sealed class OptionsFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    #endregion

    #region Server address

    [Theory]
    [InlineData("https://example.com/radarr/", true, "example.com", 443, "/radarr")]
    [InlineData("http://192.168.1.5:7878", false, "192.168.1.5", 7878, "")]
    [InlineData("https://radarr.example.com:9898", true, "radarr.example.com", 9898, "")]
    public void PastedUrl_IsSplitIntoFields(string pasted, bool https, string host, int port, string urlBase)
    {
        var server = new MediaServer { Host = pasted, Port = 1 };

        server.NormalizeAddress();

        Assert.Equal(https, server.UseHttps);
        Assert.Equal(host, server.Host);
        Assert.Equal(port, server.Port);
        Assert.Equal(urlBase, server.UrlBase);
    }

    [Fact]
    public void BaseUrl_IncludesTheUrlBase()
    {
        var server = new MediaServer { Host = "example.com", Port = 443, UseHttps = true, UrlBase = "sonarr/" };

        Assert.Equal("https://example.com:443/sonarr", server.BaseUrl);
    }

    [Fact]
    public void PlainHost_IsLeftAlone()
    {
        var server = new MediaServer { Host = "radarr ", Port = 7878, UrlBase = "" };

        server.NormalizeAddress();

        Assert.Equal("radarr", server.Host);
        Assert.Equal(7878, server.Port);
        Assert.False(server.UseHttps);
    }

    #endregion

    #region Connection errors and TLS

    [Fact]
    public void CertificateError_SaysWhy_AndSuggestsSkippingValidation()
    {
        var ex = new ConnectionException("Radarr",
            new HttpRequestException("The SSL connection could not be established",
                new AuthenticationException("The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot")));

        Assert.Contains("certificate", ex.Message);
        Assert.Contains("Skip certificate validation", ex.Message);
    }

    [Fact]
    public void RefusedConnection_SaysWhy()
    {
        var ex = new ConnectionException("Sonarr",
            new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)));

        Assert.Contains("connection refused", ex.Message);
    }

    [Theory]
    [InlineData(false, ArrClientFactory.HttpClientName)]
    [InlineData(true, ArrClientFactory.InsecureHttpClientName)]
    public async Task SkipCertificateValidation_UsesTheInsecureClient(bool skip, string expectedClient)
    {
        using var factory = new SqliteInMemoryFactory();
        var bus = new DataChangeBus();
        var settings = new SettingsService(factory, NullLogger<SettingsService>.Instance, bus);
        var recorder = new RecordingFactory();
        var service = new TestServices(factory, recorder, bus: bus, health: new HealthService(factory, settings, bus))
            .Connection;

        var server = new MediaServer
        {
            Name = "Radarr", ServiceType = ServiceType.Radarr, Host = "radarr.test", Port = 9898, UseHttps = true,
            ApiKey = "key", SkipCertificateValidation = skip
        };

        await service.TestConnectionAsync(server);

        Assert.Equal(expectedClient, Assert.Single(recorder.Names));
    }

    private sealed class RecordingFactory : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Names { get; } = [];

        public HttpClient CreateClient(string name)
        {
            Names.Add(name);
            return new HttpClient(this, false);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    #endregion
}
