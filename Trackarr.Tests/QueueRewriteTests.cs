using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>
///     Regression guards for H7: overlapping queue refreshes must not duplicate rows, and one missed scan must
///     not mark an active download as Disappeared.
/// </summary>
public class QueueRewriteTests
{
    [Fact]
    public async Task OverlappingRefreshes_LeaveNoDuplicateRows()
    {
        using var factory = new SqliteFileFactory(); // real file + pooled connections, like production
        var bus = new DataChangeBus();
        var settings = new SettingsService(factory, NullLogger<SettingsService>.Instance, bus);
        var service = new TestServices(factory, new SlowQueue(), bus: bus, health: new HealthService(factory, settings, bus))
            .Media;

        var server = new MediaServer
        {
            Name = "Radarr", ServiceType = ServiceType.Radarr, Host = "radarr.test", Port = 7878, ApiKey = "key",
            IsEnabled = true, CreatedAt = DateTime.UtcNow
        };
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.GetQueueAsync(server, true)));

        await using var check = factory.CreateDbContext();
        Assert.Equal(2, await check.DownloadQueue.CountAsync(q => q.ServerId == server.Id));
    }

    [Fact]
    public async Task OneMissedScan_KeepsAttemptActive_AndSeeingItAgainResets()
    {
        using var factory = new SqliteInMemoryFactory();
        int serverId;
        await using (var db = factory.CreateDbContext())
        {
            var server = new MediaServer
            {
                Name = "Radarr", ServiceType = ServiceType.Radarr, Host = "localhost", Port = 7878
            };
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();
            serverId = server.Id;
        }

        var bus = new DataChangeBus();
        var service = new HealthService(factory,
            new SettingsService(factory, NullLogger<SettingsService>.Instance, bus), bus);
        var item = new DownloadItem
        {
            ServerId = serverId, RemoteId = 1, MovieId = 10, DownloadId = "dl-1", Title = "Movie", Size = 2000,
            SizeLeft = 1000, Protocol = "torrent"
        };
        await service.EvaluateAsync(item);

        // Missing once (say the queue was read mid-rewrite), then back.
        Assert.Empty(await service.ResolveMissingAttemptsAsync(serverId, []));
        await service.EvaluateAsync(item);

        // Missing once more: the earlier miss was reset, so this is only the first in a row.
        Assert.Empty(await service.ResolveMissingAttemptsAsync(serverId, []));

        await using var check = factory.CreateDbContext();
        var attempt = await check.DownloadAttempts.SingleAsync();
        Assert.Equal(AttemptStatus.Active, attempt.Status);
        Assert.Equal(1, attempt.MissedScans);
    }

    /// <summary>A Radarr queue of two items that takes a moment to answer, so refreshes overlap.</summary>
    private sealed class SlowQueue : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/api/v3/queue"))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            await Task.Delay(50, cancellationToken);

            var records = Enumerable.Range(1, 2).Select(id => new
            {
                id, movieId = 100 + id, title = $"Movie {id}", downloadId = $"dl-{id}", size = 2000, sizeLeft = 1000,
                status = "downloading", protocol = "torrent", languages = Array.Empty<object>(), quality = new { },
                customFormats = Array.Empty<object>(), statusMessages = Array.Empty<object>()
            });
            var body = JsonSerializer.Serialize(new { page = 1, pageSize = 100, totalRecords = 2, records });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
