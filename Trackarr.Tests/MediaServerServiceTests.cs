using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

public class MediaServerServiceTests
{
    private static MediaServerService Create(SqliteInMemoryFactory factory)
    {
        return new MediaServerService(factory, new StubHttpClientFactory(), NullLogger<MediaServerService>.Instance,
            new DataChangeBus());
    }

    [Fact]
    public async Task CreateServer_FallbackProfileId_StoresDbId_NotRemoteId()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        var profiles = new List<QualityProfile>
        {
            new() { RemoteId = 4, Name = "HD-1080p" },
            new() { RemoteId = 7, Name = "Ultra-HD" },
            new() { RemoteId = 12, Name = "Any" }
        };

        var server = new MediaServer
        {
            Name = "Test Radarr",
            Host = "localhost",
            Port = 7878,
            ApiKey = "test-key",
            ServiceType = ServiceType.Radarr,
            IsEnabled = true,
            Profiles = profiles,
            FallbackProfile = profiles[1],
            CreatedAt = DateTime.UtcNow
        };

        await service.CreateServerAsync(server);

        await using var db = await factory.CreateDbContextAsync();
        var saved = await db.MediaServers.FindAsync(server.Id);
        var savedProfile = await db.QualityProfiles.FindAsync(saved!.FallBackProfileId);

        Assert.NotNull(saved.FallBackProfileId);
        Assert.NotNull(savedProfile);
        Assert.Equal(7, savedProfile.RemoteId);
        Assert.Equal("Ultra-HD", savedProfile.Name);
    }

    [Fact]
    public async Task CreateServer_FallbackRemoteId_DiffersFromDbId()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        var profiles = new List<QualityProfile>
        {
            new() { RemoteId = 50, Name = "HD-1080p" }, new() { RemoteId = 99, Name = "Ultra-HD" }
        };

        var server = new MediaServer
        {
            Name = "Test Radarr",
            Host = "localhost",
            Port = 7878,
            ApiKey = "test-key",
            ServiceType = ServiceType.Radarr,
            IsEnabled = true,
            Profiles = profiles,
            FallbackProfile = profiles[0],
            CreatedAt = DateTime.UtcNow
        };

        await service.CreateServerAsync(server);

        await using var db = await factory.CreateDbContextAsync();
        var saved = await db.MediaServers.FindAsync(server.Id);
        var savedProfile = await db.QualityProfiles.FindAsync(saved!.FallBackProfileId);

        Assert.NotNull(savedProfile);
        Assert.NotEqual(savedProfile.RemoteId, savedProfile.Id);
        Assert.Equal(50, savedProfile.RemoteId);
    }

    [Fact]
    public async Task CreateServer_NoFallbackProfile_LeavesIdNull()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        var server = new MediaServer
        {
            Name = "Test Sonarr",
            Host = "localhost",
            Port = 8989,
            ApiKey = "test-key",
            ServiceType = ServiceType.Sonarr,
            IsEnabled = true,
            Profiles = [new() { RemoteId = 3, Name = "HD" }],
            CreatedAt = DateTime.UtcNow
        };

        await service.CreateServerAsync(server);

        await using var db = await factory.CreateDbContextAsync();
        var saved = await db.MediaServers.FindAsync(server.Id);

        Assert.NotNull(saved);
        Assert.Null(saved.FallBackProfileId);
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler());
    }

    private class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";

            if (path.EndsWith("/api/v3/system/status"))
            {
                var body = JsonSerializer.Serialize(
                    new { appName = "Radarr", version = "5.0.0", instanceName = "test" });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            if (path.EndsWith("/api/v3/qualityprofile"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
