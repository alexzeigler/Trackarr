using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Health;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>Regression guards for the Medium findings (M1-M9) of the November health report.</summary>
public class MediumFixTests
{
    #region Setup

    private static async Task<(SqliteInMemoryFactory, HealthService, SettingsService, int)> CreateAsync(
        ServiceType type = ServiceType.Radarr)
    {
        var factory = new SqliteInMemoryFactory();
        var bus = new DataChangeBus();
        var settings = new SettingsService(factory, NullLogger<SettingsService>.Instance, bus);
        await settings.InitializeAsync();

        await using var db = factory.CreateDbContext();
        var server = new MediaServer { Name = "Server", ServiceType = type, Host = "localhost", Port = 1 };
        db.MediaServers.Add(server);
        await db.SaveChangesAsync();

        return (factory, new HealthService(factory, settings, bus), settings, server.Id);
    }

    private static DownloadItem MovieItem(int serverId, string downloadId = "dl-1") => new()
    {
        ServerId = serverId, RemoteId = 1, MovieId = 10, DownloadId = downloadId, Title = "Movie",
        Size = 2000, SizeLeft = 1000, Protocol = "torrent", Indexer = "Idx", DownloadClient = "qbit"
    };

    #endregion

    [Fact]
    public async Task M1_SonarrDownload_IsTrackedPerEpisode_AndRolledUpToSeries()
    {
        var (factory, service, _, serverId) = await CreateAsync(ServiceType.Sonarr);
        using var _ = factory;

        int seriesDbId, episodeDbId;
        await using (var db = factory.CreateDbContext())
        {
            var series = new Series { RemoteId = 5, Title = "Show", ServerId = serverId };
            db.Series.Add(series);
            await db.SaveChangesAsync();
            var episode = new Episode { RemoteId = 50, RemoteSeriesId = 5, ServerId = serverId };
            db.Episodes.Add(episode);
            await db.SaveChangesAsync();
            seriesDbId = series.Id;
            episodeDbId = episode.Id;
        }

        var item = new DownloadItem
        {
            ServerId = serverId, RemoteId = 1, SeriesId = 5, EpisodeId = 50, DownloadId = "dl-ep",
            Title = "Show.S01E01", Size = 2000, SizeLeft = 1000, Protocol = "torrent"
        };
        // Summary counts come from saved attempts, so the new attempt shows up from the second scan.
        await service.EvaluateAsync(item);
        await service.EvaluateAsync(item);

        await using var check = factory.CreateDbContext();
        var attempt = await check.DownloadAttempts.SingleAsync();
        Assert.Equal(TrackedMediaType.Episode, attempt.MediaType);
        Assert.Equal(episodeDbId, attempt.EpisodeId);
        Assert.Equal(seriesDbId, attempt.SeriesId);

        var seriesSummary = await check.MediaHealthSummaries.SingleAsync(s => s.MediaType == TrackedMediaType.Series);
        Assert.Equal(1, seriesSummary.AttemptCount); // the episode attempt is rolled up to the series
        Assert.Single(check.MediaHealthSummaries.Where(s => s.MediaType == TrackedMediaType.Episode));
    }

    [Fact]
    public void M3_Window_CountsMergedScans()
    {
        DownloadHealthEvent[] newestFirst =
        [
            new() { IsBadObservation = true, ScanCount = 6 }, // six identical stalled scans merged into one
            new() { IsGoodObservation = true, ScanCount = 1 },
            new() { IsBadObservation = true, ScanCount = 5 }
        ];

        var (bad, good) = HealthService.CountScansInWindow(newestFirst, 9);

        Assert.Equal(8, bad); // 6 + the first 2 of the oldest event's 5 (the window is full at 9)
        Assert.Equal(1, good);
    }

    [Fact]
    public async Task M3_NoChangeScan_IncrementsScanCountOnTheMergedEvent()
    {
        var (factory, service, _, serverId) = await CreateAsync();
        using var _ = factory;
        var item = MovieItem(serverId);

        for (var i = 0; i < 4; i++) await service.EvaluateAsync(item); // first seen, then three unchanged scans

        await using var db = factory.CreateDbContext();
        Assert.Equal(4, await db.HealthEvents.SumAsync(e => e.ScanCount));
    }

    [Fact]
    public async Task M4_SourceSummary_CountsAnAttemptOncePerReason()
    {
        var (factory, service, _, serverId) = await CreateAsync();
        using var _ = factory;
        var item = MovieItem(serverId);

        // Alternate the size left so every scan is a real (non-merged) change with the same reason.
        for (var i = 0; i < 6; i++)
        {
            item.SizeLeft = 1000 - i * 100;
            await service.EvaluateAsync(item);
        }

        await using var db = factory.CreateDbContext();
        Assert.All(await db.SourceHealthSummaries.ToListAsync(), s => Assert.Equal(1, s.AttemptCount));
    }

    [Fact]
    public async Task M5_PassiveMode_IsARestriction_AndNeverShowsRemove()
    {
        var (factory, service, settings, serverId) = await CreateAsync();
        using var _ = factory;
        await settings.SaveCategoryAsync("General", new Dictionary<string, string> { ["PassiveMode"] = "true" });

        var decision = await service.EvaluateAsync(MovieItem(serverId));

        Assert.False(decision.ShouldRemove);
        Assert.Contains(decision.Restrictions, r => r.Contains("Passive Mode"));
        await using var db = factory.CreateDbContext();
        Assert.NotEqual(HealthState.Remove, (await db.HealthSnapshots.SingleAsync()).State);
    }

    [Fact]
    public async Task M7_DuplicateMovieMissingRecord_IsRejected()
    {
        var (factory, _, _, serverId) = await CreateAsync();
        using var _ = factory;

        await using var db = factory.CreateDbContext();
        db.MissingMedia.Add(new MissingMediaRecord { ServerId = serverId, RemoteId = 7 });
        await db.SaveChangesAsync();
        db.MissingMedia.Add(new MissingMediaRecord { ServerId = serverId, RemoteId = 7 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task M8_History_SurvivesOrphanedRowsWithTheSameRemoteId()
    {
        var (factory, _, _, serverId) = await CreateAsync();
        using var _ = factory;

        await using (var db = factory.CreateDbContext())
        {
            // Two rows left behind by deleted servers, plus a search event for the live server.
            db.Movies.Add(new Movie { RemoteId = 3, Title = "Orphan A", ServerId = null });
            db.Movies.Add(new Movie { RemoteId = 3, Title = "Orphan B", ServerId = null });
            db.Movies.Add(new Movie { RemoteId = 3, Title = "Live", ServerId = serverId });
            db.SearchEvents.Add(new MissingSearchEvent
            {
                ServerId = serverId, RemoteId = 3, EventType = MissingEventType.SearchPushed
            });
            await db.SaveChangesAsync();
        }

        var history = new HistoryService(factory);
        var page = await history.GetHistoryAsync(new HistoryFilter
        {
            From = DateTime.UtcNow.AddDays(-1), To = DateTime.UtcNow.AddDays(1)
        });

        Assert.NotNull(page);
    }

    [Fact]
    public async Task M9_SavingServer_KeepsStatusWrittenSinceThePageLoaded()
    {
        using var factory = new SqliteInMemoryFactory();
        var bus = new DataChangeBus();
        var settings = new SettingsService(factory, NullLogger<SettingsService>.Instance, bus);
        var media = new MediaServerService(factory, new NoHttp(), NullLogger<MediaServerService>.Instance, bus,
            new HealthService(factory, settings, bus));

        var server = new MediaServer
        {
            Name = "Radarr", ServiceType = ServiceType.Radarr, Host = "radarr.test", Port = 1, ApiKey = "k",
            IsEnabled = true
        };
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();
        }

        // The page loaded `server`; meanwhile a routine records a queue fetch.
        await media.UpdateServerStatsAsync(server.Id, s => s.LastQueueCount = 42);

        server.Name = "Renamed"; // the user's edit
        await media.UpdateServerAsync(server);

        await using var check = factory.CreateDbContext();
        var saved = await check.MediaServers.SingleAsync();
        Assert.Equal("Renamed", saved.Name);
        Assert.Equal(42, saved.LastQueueCount);
    }

    [Fact]
    public async Task M8_DeletingServer_DeletesItsMediaAndHistory()
    {
        var (factory, service, settings, serverId) = await CreateAsync();
        using var _ = factory;
        await using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { RemoteId = 10, Title = "Movie", ServerId = serverId });
            db.QualityProfiles.Add(new QualityProfile { RemoteId = 1, Name = "HD", ServerId = serverId });
            await db.SaveChangesAsync();
        }

        await service.EvaluateAsync(MovieItem(serverId));

        var bus = new DataChangeBus();
        var media = new MediaServerService(factory, new NoHttp(), NullLogger<MediaServerService>.Instance, bus,
            service, settings);
        await media.DeleteServerAsync(serverId);

        await using var check = factory.CreateDbContext();
        Assert.Empty(await check.Movies.ToListAsync());
        Assert.Empty(await check.QualityProfiles.ToListAsync());
        Assert.Empty(await check.DownloadAttempts.ToListAsync());
        Assert.Empty(await check.HealthEvents.ToListAsync());
        Assert.Empty(await check.HealthSnapshots.ToListAsync());
    }

    [Fact]
    public async Task M13_Preview_SavesNothing()
    {
        var (factory, service, _, serverId) = await CreateAsync();
        using var _ = factory;
        var item = MovieItem(serverId);
        await service.EvaluateAsync(item);

        await using (var before = factory.CreateDbContext())
        {
            var snapshot = await before.HealthSnapshots.AsNoTracking().SingleAsync();
            var events = await before.HealthEvents.SumAsync(e => e.ScanCount);

            for (var i = 0; i < 5; i++)
            {
                var decision = await service.PreviewAsync(item);
                Assert.False(decision.ShouldRemove);
            }

            await using var after = factory.CreateDbContext();
            Assert.Equal(events, await after.HealthEvents.SumAsync(e => e.ScanCount));
            var snapshotAfter = await after.HealthSnapshots.AsNoTracking().SingleAsync();
            Assert.Equal(snapshot.RawScore, snapshotAfter.RawScore);
            Assert.Equal(snapshot.BadScanCount, snapshotAfter.BadScanCount);
        }
    }

    [Fact]
    public async Task P3_BatchedQueueHealth_MatchesThePerItemReads()
    {
        var (factory, service, _, serverId) = await CreateAsync();
        using var _ = factory;
        await using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { RemoteId = 10, Title = "Movie", ServerId = serverId });
            db.Movies.Add(new Movie { RemoteId = 11, Title = "Other", ServerId = serverId });
            await db.SaveChangesAsync();
        }

        var a = MovieItem(serverId, "dl-a");
        var b = MovieItem(serverId, "dl-b");
        b.RemoteId = 2;
        b.MovieId = 11;
        foreach (var i in Enumerable.Range(0, 3))
        {
            a.SizeLeft = 1000 - i * 100;
            await service.EvaluateAsync(a);
            await service.EvaluateAsync(b);
        }

        var (snapshots, summaries, events) =
            await service.GetQueueHealthAsync([a, b], i => $"{i.ServerId}_{i.RemoteId}");

        foreach (var item in new[] { a, b })
        {
            var key = $"{item.ServerId}_{item.RemoteId}";
            Assert.Equal((await service.GetMostRecentSnapshotAsync(item))?.Id, snapshots[key].Id);
            Assert.Equal((await service.GetMostRecentHealthEventAsync(item))?.Id, events[key].Id);
            Assert.Equal((await service.GetMediaSummary(item))?.Id, summaries[key].Id);
        }
    }

    [Fact]
    public async Task Protect_BlocksRemoval_AndUnprotectLiftsIt()
    {
        var (factory, service, _, serverId) = await CreateAsync();
        using var _ = factory;
        var item = MovieItem(serverId);

        Assert.False(await service.SetProtectedAsync(item, true)); // not scanned yet: nothing to protect

        await service.EvaluateAsync(item);
        Assert.True(await service.SetProtectedAsync(item, true));

        var decision = await service.EvaluateAsync(item);
        Assert.False(decision.ShouldRemove);
        Assert.Contains("Download is protected.", decision.Restrictions);

        Assert.True(await service.SetProtectedAsync(item, false));
        decision = await service.EvaluateAsync(item);
        Assert.DoesNotContain("Download is protected.", decision.Restrictions);

        await using var db = factory.CreateDbContext();
        Assert.Equal(2, await db.HealthEvents.CountAsync(e => e.EventType == HealthEventType.ManualOverride));
    }

    private sealed class NoHttp : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new HttpRequestException("offline");
    }
}
