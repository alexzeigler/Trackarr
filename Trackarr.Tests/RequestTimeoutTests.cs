using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>Regression guard for H6: the Request Timeout setting was never read; timeouts were hardcoded.</summary>
public class RequestTimeoutTests
{
    [Fact]
    public async Task ServerRequestTimeout_IsUsedForApiCalls()
    {
        using var factory = new SqliteInMemoryFactory();
        var bus = new DataChangeBus();
        var settings = new SettingsService(factory, NullLogger<SettingsService>.Instance, bus);
        await settings.InitializeAsync();
        var health = new HealthService(factory, settings, bus);
        var service = new TestServices(factory, new HangingHandler(), settings, bus, health).Connection;

        // 1 second here, against the 10 second value TestConnection used to hardcode.
        var server = new MediaServer
        {
            Name = "Slow Radarr", ServiceType = ServiceType.Radarr, Host = "slow.test", Port = 7878,
            ApiKey = "key", IsEnabled = true, RequestTimeoutSeconds = 1
        };

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<ConnectionException>(() => service.TestConnectionAsync(server));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Timed out after {sw.Elapsed}, expected about 1s");
    }

    /// <summary>Never answers within the test's lifetime.</summary>
    private sealed class HangingHandler : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
