using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>
///     Regression guards for H5: Servers page actions against a server that is down or rejects the API key.
/// </summary>
public class ServerActionFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task TestConnection_RejectedApiKey_MarksNotAuthenticated(HttpStatusCode status)
    {
        using var factory = new SqliteInMemoryFactory();
        var (service, server) = await CreateAsync(factory, new FixedHandler(status));

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.TestConnectionAsync(server));
        Assert.True(ex.IsAuthFailure);

        Assert.False(server.IsAuthenticated);

        await using var db = factory.CreateDbContext();
        var saved = await db.MediaServers.SingleAsync();
        Assert.False(saved.IsAuthenticated);
        Assert.True(saved.IsOnline); // it answered, so it's reachable
        Assert.NotNull(saved.LastError);
    }

    [Fact]
    public async Task UpdateServer_UnreachableServer_StillSavesTheChange()
    {
        using var factory = new SqliteInMemoryFactory();
        var (service, server) = await CreateAsync(factory, new FixedHandler(null));

        server.IsEnabled = false;
        await service.UpdateServerAsync(server); // must not throw just because the server is down

        await using var db = factory.CreateDbContext();
        Assert.False((await db.MediaServers.SingleAsync()).IsEnabled);
    }

    private static async Task<(MediaServerService, MediaServer)> CreateAsync(SqliteInMemoryFactory factory,
        FixedHandler handler)
    {
        var bus = new DataChangeBus();
        var settings = new SettingsService(factory, NullLogger<SettingsService>.Instance, bus);
        var service = new MediaServerService(factory, handler, NullLogger<MediaServerService>.Instance, bus,
            new HealthService(factory, settings, bus));

        var server = new MediaServer
        {
            Name = "Radarr", ServiceType = ServiceType.Radarr, Host = "radarr.test", Port = 7878,
            ApiKey = "wrong-key", IsEnabled = true, IsAuthenticated = true, CreatedAt = DateTime.UtcNow
        };
        await using var db = factory.CreateDbContext();
        db.MediaServers.Add(server);
        await db.SaveChangesAsync();

        return (service, server);
    }

    /// <summary>Answers every request with one status code, or throws like an unreachable host when null.</summary>
    private sealed class FixedHandler(HttpStatusCode? status) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (status is null) throw new HttpRequestException("Connection refused");
            return Task.FromResult(new HttpResponseMessage(status.Value));
        }
    }
}
