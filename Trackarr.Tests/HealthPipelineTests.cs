using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;

namespace Trackarr.Tests;

/// <summary>
///     End-to-end tests of the refactored health pipeline (HealthService -> HealthRepository + HealthEvaluator)
///     against a real in-memory SQLite database.
/// </summary>
public class HealthPipelineTests
{
    private static async Task<int> SeedServerAsync(SqliteInMemoryFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var server = new MediaServer
        {
            Name = "Test Radarr", ServiceType = ServiceType.Radarr, Host = "localhost", Port = 7878
        };
        db.MediaServers.Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static DownloadItem MovieDownload(int serverId, long sizeLeft = 1000)
    {
        return new DownloadItem
        {
            ServerId = serverId,
            RemoteId = 55,
            MovieId = 900,
            DownloadId = "dl-abc",
            Title = "Some Movie",
            Size = 2000,
            SizeLeft = sizeLeft,
            Protocol = "torrent"
        };
    }

    [Fact]
    public async Task GetOrCreateAttempt_IsIdempotentForSameDownload()
    {
        using var factory = new SqliteInMemoryFactory();
        var serverId = await SeedServerAsync(factory);
        var repo = new HealthRepository();
        var now = DateTime.UtcNow;
        var item = MovieDownload(serverId);

        int firstId;
        await using (var db = factory.CreateDbContext())
        {
            var attempt = await repo.GetOrCreateAttemptAsync(db, item, null, now, CancellationToken.None);
            await db.SaveChangesAsync();
            firstId = attempt.Id;
        }

        await using (var db = factory.CreateDbContext())
        {
            var again = await repo.GetOrCreateAttemptAsync(db, item, null, now, CancellationToken.None);
            await db.SaveChangesAsync();

            Assert.Equal(firstId, again.Id);
            Assert.Equal(1, await db.DownloadAttempts.CountAsync());
        }
    }

    [Fact]
    public async Task GetOrCreateSnapshot_StartsAtPolicyStartingScore()
    {
        using var factory = new SqliteInMemoryFactory();
        var serverId = await SeedServerAsync(factory);
        var repo = new HealthRepository();
        var policy = new HealthPolicy();
        var now = DateTime.UtcNow;

        await using var db = factory.CreateDbContext();
        var attempt =
            await repo.GetOrCreateAttemptAsync(db, MovieDownload(serverId), null, now, CancellationToken.None);
        await db.SaveChangesAsync();

        var snapshot = await repo.GetOrCreateSnapshotAsync(db, attempt, now, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(policy.StartingScore, snapshot.VisibleScore);
        Assert.Equal(HealthState.New, snapshot.State);
    }

    [Fact]
    public async Task EvaluateAsync_FirstObservation_PersistsAttemptSnapshotEventSummary()
    {
        using var factory = new SqliteInMemoryFactory();
        var serverId = await SeedServerAsync(factory);
        var service = new HealthService(factory, new SettingsService(factory, NullLogger<SettingsService>.Instance));

        var decision = await service.EvaluateAsync(MovieDownload(serverId));

        Assert.True(decision.DownloadAttemptId > 0);
        Assert.False(decision.ShouldRemove); // brand-new download is inside the startup grace period
        Assert.NotEmpty(decision.Restrictions);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.DownloadAttempts.CountAsync());
        Assert.Equal(1, await db.HealthSnapshots.CountAsync());
        Assert.Equal(1, await db.HealthEvents.CountAsync());
        Assert.Equal(1, await db.MediaHealthSummaries.CountAsync());

        var ev = await db.HealthEvents.SingleAsync();
        Assert.Equal(HealthEventType.FirstSeen, ev.EventType);
    }

    [Fact]
    public async Task EvaluateAsync_RepeatedDownload_ReusesSingleAttempt()
    {
        using var factory = new SqliteInMemoryFactory();
        var serverId = await SeedServerAsync(factory);
        var service = new HealthService(factory, new SettingsService(factory, NullLogger<SettingsService>.Instance));

        await service.EvaluateAsync(MovieDownload(serverId));
        await service.EvaluateAsync(MovieDownload(serverId));

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.DownloadAttempts.CountAsync());
        Assert.Equal(2, await db.HealthEvents.CountAsync());
    }

    [Fact]
    public async Task EvaluateAsync_SecondObservationWithProgress_RaisesScore()
    {
        using var factory = new SqliteInMemoryFactory();
        var serverId = await SeedServerAsync(factory);
        var service = new HealthService(factory, new SettingsService(factory, NullLogger<SettingsService>.Instance));
        var policy = new HealthPolicy();

        await service.EvaluateAsync(MovieDownload(serverId, 1000)); // first seen: score stays at start
        await service.EvaluateAsync(MovieDownload(serverId, 500)); // progress: reward

        await using var db = factory.CreateDbContext();
        var snapshot = await db.HealthSnapshots.SingleAsync();
        Assert.True(snapshot.RawScore > policy.StartingScore);
    }
}
