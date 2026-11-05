using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

public class MediaServerServiceTests
{
    private static MediaServerService Create(SqliteInMemoryFactory factory)
    {
        var changeBus = new DataChangeBus();
        var settings = new SettingsService(factory, NullLogger<SettingsService>.Instance, changeBus);
        var healthService = new HealthService(factory, settings, changeBus);

        return new MediaServerService(factory, new StubHttpClientFactory(), NullLogger<MediaServerService>.Instance,
            changeBus, healthService);
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
    public async Task SyncServerAsync_EvaluatesHealthForQueuedItems()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        var server = new MediaServer
        {
            Name = "Synced Radarr",
            Host = "localhost",
            Port = 7878,
            ApiKey = "test-key",
            ServiceType = ServiceType.Radarr,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        await service.CreateServerAsync(server);
        await service.SyncServerAsync(server);

        await using var db = await factory.CreateDbContextAsync();
        var attempts = await db.DownloadAttempts.Where(a => a.ServerId == server.Id).ToListAsync();

        Assert.Single(attempts);
        Assert.Equal("queue-item-1", attempts[0].DownloadId);
    }

    [Fact]
    public async Task GetQueueAsync_FreshFetch_ScoresNewDownloadsOnce()
    {
        // Whoever fetches the queue (Queue page, routine, sync), a new download is scored right away, and only
        // once: fetching again doesn't add scans for downloads already being tracked.
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        var server = new MediaServer
        {
            Name = "Radarr",
            Host = "localhost",
            Port = 7878,
            ApiKey = "test-key",
            ServiceType = ServiceType.Radarr,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        await service.CreateServerAsync(server);

        await service.GetQueueAsync(server, true);
        await service.GetQueueAsync(server, true);

        await using var db = await factory.CreateDbContextAsync();
        var attempt = await db.DownloadAttempts.SingleAsync(a => a.ServerId == server.Id);
        Assert.Equal(1, await db.HealthEvents.Where(e => e.DownloadAttemptId == attempt.Id).SumAsync(e => e.ScanCount));
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

            if (path.EndsWith("/api/v3/queue"))
            {
                var body = JsonSerializer.Serialize(new
                {
                    page = 1,
                    pageSize = 100,
                    totalRecords = 1,
                    records = new[]
                    {
                        new
                        {
                            id = 1,
                            movieId = 0,
                            seriesId = 0,
                            episodeId = 0,
                            size = 1_000_000,
                            sizeleft = 500_000,
                            title = "Some.Movie.2024.1080p",
                            added = DateTime.UtcNow,
                            status = "downloading",
                            trackedDownloadStatus = "ok",
                            trackedDownloadState = "downloading",
                            downloadId = "queue-item-1",
                            protocol = "torrent",
                            downloadClient = "qBittorrent",
                            indexer = "Prowlarr",
                            languages = Array.Empty<object>(),
                            quality = new { quality = new { id = 7, name = "Bluray-1080p" } },
                            customFormats = Array.Empty<object>(),
                            statusMessages = Array.Empty<object>()
                        }
                    }
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            if (path.EndsWith("/api/v3/wanted/missing"))
            {
                var body = JsonSerializer.Serialize(new
                {
                    page = 1, pageSize = 100, totalRecords = 0, records = Array.Empty<object>()
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
