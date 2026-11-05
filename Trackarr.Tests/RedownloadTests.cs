using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

public class RedownloadTests
{
    private static async Task<(SqliteInMemoryFactory Factory, int ServerId)> SetupAsync()
    {
        var factory = new SqliteInMemoryFactory();
        await using var db = factory.CreateDbContext();
        var server = new MediaServer
        {
            Name = "Test", ServiceType = ServiceType.Radarr, Host = "localhost", Port = 7878
        };
        db.MediaServers.Add(server);
        await db.SaveChangesAsync();
        return (factory, server.Id);
    }

    private static HealthService CreateService(SqliteInMemoryFactory factory)
    {
        return new HealthService(factory,
            new SettingsService(factory, NullLogger<SettingsService>.Instance, new DataChangeBus()),
            new DataChangeBus());
    }

    private static DownloadItem MakeItem(int serverId, string downloadId = "dl-1", int remoteId = 100,
        int movieId = 900, long sizeLeft = 1000)
    {
        return new DownloadItem
        {
            ServerId = serverId,
            RemoteId = remoteId,
            MovieId = movieId,
            DownloadId = downloadId,
            Title = "Test Movie",
            Size = 2000,
            SizeLeft = sizeLeft,
            Protocol = "torrent"
        };
    }

    private static string GetItemKey(DownloadItem item) => $"{item.ServerId}_{item.RemoteId}";

    #region Re-download after terminal states

    [Fact]
    public async Task Redownload_AfterFailure_CreatesNewAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId));
        await service.MarkFailedAsync(first.DownloadAttemptId, "disk-full", "Disk full");

        var second = await service.EvaluateAsync(MakeItem(serverId));

        Assert.NotEqual(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    [Fact]
    public async Task Redownload_AfterTrackarrRemoval_CreatesNewAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId));
        await service.MarkRemovedByTrackarrAsync(first.DownloadAttemptId);

        var second = await service.EvaluateAsync(MakeItem(serverId));

        Assert.NotEqual(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    [Fact]
    public async Task Redownload_AfterDisappeared_CreatesNewAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item = MakeItem(serverId);
        await service.EvaluateAsync(item);
        await service.ResolveMissingAttemptsAsync(serverId, []);
        await service.ResolveMissingAttemptsAsync(serverId, []); // Disappeared needs two misses in a row

        var second = await service.EvaluateAsync(item);

        await using var db = factory.CreateDbContext();
        var attempts = await db.DownloadAttempts.ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Single(attempts, a => a.Status == AttemptStatus.Disappeared);
        Assert.Single(attempts, a => a.Status == AttemptStatus.Active);
    }

    [Fact]
    public async Task Redownload_AfterImported_CreatesNewAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId));
        await service.MarkImportedAsync(first.DownloadAttemptId);

        var second = await service.EvaluateAsync(MakeItem(serverId));

        Assert.NotEqual(first.DownloadAttemptId, second.DownloadAttemptId);

        await using var db = factory.CreateDbContext();
        var old = await db.DownloadAttempts.FirstAsync(a => a.Id == first.DownloadAttemptId);
        Assert.True(old.WasSuccessful);
    }

    #endregion

    #region Re-download while still active

    [Fact]
    public async Task Redownload_WhileStillActive_SameDownloadId_ReusesSameAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId));
        var second = await service.EvaluateAsync(MakeItem(serverId));

        Assert.Equal(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    [Fact]
    public async Task Redownload_WhileStillActive_DifferentDownloadId_CreatesNewAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-1"));
        var second = await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-2"));

        Assert.NotEqual(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    #endregion

    #region Same release vs different release

    [Fact]
    public async Task Redownload_DifferentRelease_IncrementsReplacementAttemptNumber()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-1"));
        await service.MarkFailedAsync(first.DownloadAttemptId, "stalled", "Stalled");

        await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-2"));

        await using var db = factory.CreateDbContext();
        var attempts = await db.DownloadAttempts.OrderBy(a => a.Id).ToListAsync();

        Assert.Equal(2, attempts.Count);
        Assert.Equal(1, attempts[0].ReplacementAttemptNumber);
        Assert.Equal(2, attempts[1].ReplacementAttemptNumber);
    }

    [Fact]
    public async Task Redownload_ThreeConsecutiveFailures_AttemptNumbersAreSequential()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        for (var i = 1; i <= 3; i++)
        {
            var decision = await service.EvaluateAsync(MakeItem(serverId, downloadId: $"dl-{i}"));
            await service.MarkFailedAsync(decision.DownloadAttemptId, "err", "Error");
        }

        await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-4"));

        await using var db = factory.CreateDbContext();
        var attempts = await db.DownloadAttempts.OrderBy(a => a.Id).ToListAsync();

        Assert.Equal(4, attempts.Count);
        Assert.Equal(1, attempts[0].ReplacementAttemptNumber);
        Assert.Equal(2, attempts[1].ReplacementAttemptNumber);
        Assert.Equal(3, attempts[2].ReplacementAttemptNumber);
        Assert.Equal(4, attempts[3].ReplacementAttemptNumber);
    }

    #endregion

    #region Health snapshot isolation

    [Fact]
    public async Task Redownload_AfterFailure_GetsCleanSnapshot()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId));
        await service.MarkFailedAsync(first.DownloadAttemptId, "stalled", "Stalled");

        var second = await service.EvaluateAsync(MakeItem(serverId));
        var snapshot = await service.GetSnapshotAsync(second.DownloadAttemptId);

        Assert.NotNull(snapshot);
        Assert.NotEqual(HealthState.Failed, snapshot.State);
    }

    [Fact]
    public async Task Redownload_AfterFailure_OldSnapshotPreserved()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId));
        await service.MarkFailedAsync(first.DownloadAttemptId, "stalled", "Stalled");

        await service.EvaluateAsync(MakeItem(serverId));

        var oldSnapshot = await service.GetSnapshotAsync(first.DownloadAttemptId);
        Assert.NotNull(oldSnapshot);
        Assert.Equal(HealthState.Failed, oldSnapshot.State);
    }

    [Fact]
    public async Task Redownload_AfterTrackarrRemoval_OldEventsNotSharedWithNewAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId));
        await service.MarkRemovedByTrackarrAsync(first.DownloadAttemptId);

        var second = await service.EvaluateAsync(MakeItem(serverId));
        var newEvents = await service.GetEventsAsync(second.DownloadAttemptId);
        var oldEvents = await service.GetEventsAsync(first.DownloadAttemptId);

        Assert.All(newEvents, e => Assert.Equal(second.DownloadAttemptId, e.DownloadAttemptId));
        Assert.All(oldEvents, e => Assert.Equal(first.DownloadAttemptId, e.DownloadAttemptId));
        Assert.DoesNotContain(newEvents, e => e.EventType == HealthEventType.RemovedByTrackarr);
    }

    #endregion

    #region RemoteId recycling and key collisions

    [Fact]
    public async Task Redownload_SameRemoteId_DifferentDownloadId_CreatesSeparateAttempts()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-1", remoteId: 500));
        await service.MarkFailedAsync(first.DownloadAttemptId, "err", "Error");

        var second = await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-2", remoteId: 500));

        Assert.NotEqual(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    [Fact]
    public async Task Redownload_SameRemoteId_SameDownloadId_ReusesAttemptWhileActive()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-1", remoteId: 500));
        var second = await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-1", remoteId: 500));

        Assert.Equal(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    [Fact]
    public async Task Redownload_NewRemoteId_SameDownloadId_SameAttemptWhileActive()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-1", remoteId: 100));
        var second = await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-1", remoteId: 200));

        Assert.Equal(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    #endregion

    #region Null/missing DownloadId fallback

    [Fact]
    public async Task Redownload_NullDownloadId_SameRemoteAndTitle_ReusesFallbackKey()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item1 = MakeItem(serverId, remoteId: 42);
        item1.DownloadId = null;

        var item2 = MakeItem(serverId, remoteId: 42);
        item2.DownloadId = null;

        var first = await service.EvaluateAsync(item1);
        var second = await service.EvaluateAsync(item2);

        Assert.Equal(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    [Fact]
    public async Task Redownload_NullDownloadId_DifferentRemoteId_CreatesNewAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item1 = MakeItem(serverId, remoteId: 42);
        item1.DownloadId = null;
        var first = await service.EvaluateAsync(item1);
        await service.MarkFailedAsync(first.DownloadAttemptId, "err", "Error");

        var item2 = MakeItem(serverId, remoteId: 99);
        item2.DownloadId = null;
        var second = await service.EvaluateAsync(item2);

        Assert.NotEqual(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    [Fact]
    public async Task Redownload_EmptyDownloadId_UsesFallbackKey()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item = MakeItem(serverId, remoteId: 42);
        item.DownloadId = "";

        var first = await service.EvaluateAsync(item);
        var second = await service.EvaluateAsync(item);

        Assert.Equal(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    [Fact]
    public async Task Redownload_WhitespaceDownloadId_UsesFallbackKey()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item = MakeItem(serverId, remoteId: 42);
        item.DownloadId = "   ";

        var first = await service.EvaluateAsync(item);
        var second = await service.EvaluateAsync(item);

        Assert.Equal(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    #endregion

    #region ResolveMissing + re-download interaction

    [Fact]
    public async Task Redownload_AfterResolveMissing_AsImported_NewAttemptIsClean()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item = MakeItem(serverId);
        await service.EvaluateAsync(item);

        await service.ResolveMissingAttemptsAsync(serverId, [], [item.DownloadId!]);

        var second = await service.EvaluateAsync(item);
        var snapshot = await service.GetSnapshotAsync(second.DownloadAttemptId);

        Assert.NotNull(snapshot);
        Assert.NotEqual(HealthState.Resolved, snapshot.State);
    }

    [Fact]
    public async Task Redownload_AfterResolveMissing_AsDisappeared_NewAttemptIsClean()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item = MakeItem(serverId);
        await service.EvaluateAsync(item);

        await service.ResolveMissingAttemptsAsync(serverId, []);
        await service.ResolveMissingAttemptsAsync(serverId, []); // Disappeared needs two misses in a row

        var second = await service.EvaluateAsync(item);
        var snapshot = await service.GetSnapshotAsync(second.DownloadAttemptId);

        Assert.NotNull(snapshot);
        Assert.NotEqual(HealthState.Unknown, snapshot.State);
    }

    #endregion

    #region Queue page deduplication and key consistency

    [Fact]
    public void DistinctBy_SameDownloadId_DifferentRemoteId_KeepsOnlyFirst()
    {
        var items = new List<DownloadItem>
        {
            MakeItem(1, downloadId: "dl-1", remoteId: 100),
            MakeItem(1, downloadId: "dl-1", remoteId: 200)
        };

        var deduped = items.DistinctBy(d => $"{d.ServerId}_{d.DownloadId}").ToList();

        Assert.Single(deduped);
        Assert.Equal(100, deduped[0].RemoteId);
    }

    [Fact]
    public void DistinctBy_DifferentDownloadId_SameRemoteId_KeepsBoth()
    {
        var items = new List<DownloadItem>
        {
            MakeItem(1, downloadId: "dl-1", remoteId: 100),
            MakeItem(1, downloadId: "dl-2", remoteId: 100)
        };

        var deduped = items.DistinctBy(d => $"{d.ServerId}_{d.DownloadId}").ToList();

        Assert.Equal(2, deduped.Count);
    }

    [Fact]
    public void DistinctBy_DifferentDownloadId_SameRemoteId_ProducesDuplicateItemKeys()
    {
        var items = new List<DownloadItem>
        {
            MakeItem(1, downloadId: "dl-1", remoteId: 100),
            MakeItem(1, downloadId: "dl-2", remoteId: 100)
        };

        var deduped = items.DistinctBy(d => $"{d.ServerId}_{d.DownloadId}").ToList();
        var keys = deduped.Select(GetItemKey).ToList();

        Assert.Equal(keys[0], keys[1]);
    }

    [Fact]
    public void ChildKey_WithIndexPrefix_NeverCollides()
    {
        var items = new List<DownloadItem>
        {
            MakeItem(1, downloadId: "dl-1", remoteId: 100),
            MakeItem(1, downloadId: "dl-2", remoteId: 100),
            MakeItem(1, downloadId: "dl-3", remoteId: 100)
        };

        var childKeys = items.Select((item, i) => $"child_{GetItemKey(item)}_{i}").ToList();

        Assert.Equal(childKeys.Count, childKeys.Distinct().Count());
    }

    [Fact]
    public void ChildKey_NeverCollidesWithGroupKey()
    {
        var groupKey = "series_1_42";
        var item = MakeItem(1, remoteId: 100);
        var childKey = $"child_{GetItemKey(item)}_0";

        Assert.NotEqual(groupKey, childKey);
        Assert.StartsWith("child_", childKey);
    }

    #endregion

    #region Snapshot dictionary lookup with re-download

    [Fact]
    public async Task SnapshotLookup_NewRemoteId_DoesNotFindOldSnapshot()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var oldItem = MakeItem(serverId, downloadId: "dl-1", remoteId: 100);
        var decision = await service.EvaluateAsync(oldItem);

        var snapshots = new Dictionary<string, HealthSnapshot>();
        var oldSnapshot = await service.GetSnapshotAsync(decision.DownloadAttemptId);
        if (oldSnapshot != null) snapshots[GetItemKey(oldItem)] = oldSnapshot;

        var newItem = MakeItem(serverId, downloadId: "dl-1", remoteId: 200);
        var newKey = GetItemKey(newItem);

        Assert.False(snapshots.ContainsKey(newKey));
    }

    [Fact]
    public async Task SnapshotLookup_SameRemoteId_FindsSnapshot()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item = MakeItem(serverId, downloadId: "dl-1", remoteId: 100);
        var decision = await service.EvaluateAsync(item);

        var snapshots = new Dictionary<string, HealthSnapshot>();
        var snapshot = await service.GetSnapshotAsync(decision.DownloadAttemptId);
        if (snapshot != null) snapshots[GetItemKey(item)] = snapshot;

        Assert.True(snapshots.ContainsKey(GetItemKey(item)));
    }

    #endregion

    #region Multi-server same media

    [Fact]
    public async Task Redownload_SameMedia_DifferentServers_IndependentAttempts()
    {
        var factory = new SqliteInMemoryFactory();
        using var _ = factory;
        await using var db = factory.CreateDbContext();
        var server1 = new MediaServer
        {
            Name = "Radarr1", ServiceType = ServiceType.Radarr, Host = "host1", Port = 7878
        };
        var server2 = new MediaServer
        {
            Name = "Radarr2", ServiceType = ServiceType.Radarr, Host = "host2", Port = 7878
        };
        db.MediaServers.AddRange(server1, server2);
        await db.SaveChangesAsync();

        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(server1.Id, downloadId: "dl-1", movieId: 900));
        var second = await service.EvaluateAsync(MakeItem(server2.Id, downloadId: "dl-1", movieId: 900));

        Assert.NotEqual(first.DownloadAttemptId, second.DownloadAttemptId);
    }

    [Fact]
    public void ItemKey_DifferentServers_SameRemoteId_NeverCollides()
    {
        var item1 = MakeItem(1, remoteId: 100);
        var item2 = MakeItem(2, remoteId: 100);

        Assert.NotEqual(GetItemKey(item1), GetItemKey(item2));
    }

    #endregion

    #region Rapid remove + re-add race

    [Fact]
    public async Task Redownload_RapidReaddBeforeEvaluation_SameDownloadId_ReusesAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item = MakeItem(serverId);
        var first = await service.EvaluateAsync(item);

        var second = await service.EvaluateAsync(item);

        Assert.Equal(first.DownloadAttemptId, second.DownloadAttemptId);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.DownloadAttempts.CountAsync());
    }

    [Fact]
    public async Task Redownload_RapidReaddBeforeResolveMissing_OldAttemptStillActive()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item = MakeItem(serverId);
        var first = await service.EvaluateAsync(item);

        var activeIds = new[] { item.DownloadId! };
        var missing = await service.ResolveMissingAttemptsAsync(serverId, activeIds);

        Assert.Empty(missing);

        await using var db = factory.CreateDbContext();
        var attempt = await db.DownloadAttempts.FirstAsync(a => a.Id == first.DownloadAttemptId);
        Assert.Equal(AttemptStatus.Active, attempt.Status);
    }

    #endregion

    #region GetActiveAttempts after re-download

    [Fact]
    public async Task Redownload_OnlyNewAttemptIsActive()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId));
        await service.MarkFailedAsync(first.DownloadAttemptId, "err", "Error");

        await service.EvaluateAsync(MakeItem(serverId));

        var active = await service.GetActiveAttemptsAsync();

        Assert.Single(active);
        Assert.NotEqual(first.DownloadAttemptId, active[0].Id);
    }

    [Fact]
    public async Task Redownload_HistoryContainsBothAttempts()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId));
        await service.MarkFailedAsync(first.DownloadAttemptId, "err", "Error");

        await service.EvaluateAsync(MakeItem(serverId));

        var history = await service.GetHistoryAsync();

        Assert.Equal(2, history.Count);
    }

    #endregion

    #region GetMissingDownloadIds after re-download

    [Fact]
    public async Task GetMissing_RedownloadedItem_NotReportedAsMissing()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-1"));
        await service.MarkFailedAsync(first.DownloadAttemptId, "err", "Error");

        await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-1"));

        var missing = await service.GetMissingDownloadIdsAsync(serverId, ["dl-1"]);
        Assert.Empty(missing);
    }

    [Fact]
    public async Task GetMissing_DifferentRelease_OldIdNotReportedIfTerminal()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var first = await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-1"));
        await service.MarkFailedAsync(first.DownloadAttemptId, "err", "Error");

        await service.EvaluateAsync(MakeItem(serverId, downloadId: "dl-2"));

        var missing = await service.GetMissingDownloadIdsAsync(serverId, ["dl-2"]);
        Assert.Empty(missing);
    }

    #endregion

    #region MarkRemovedByUser then re-download

    [Fact]
    public async Task Redownload_AfterUserRemoval_CreatesNewAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item = MakeItem(serverId);
        await service.EvaluateAsync(item);
        await service.MarkRemovedByUserAsync(item);

        var second = await service.EvaluateAsync(item);

        await using var db = factory.CreateDbContext();
        var attempts = await db.DownloadAttempts.ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Single(attempts, a => a.Status == AttemptStatus.Active);
        Assert.Single(attempts, a => a.Status == AttemptStatus.RemovedByTrackarr);
    }

    #endregion

    #region SetScore then re-download

    [Fact]
    public async Task Redownload_AfterManualScoreOverride_NewAttemptIgnoresOldScore()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item = MakeItem(serverId);
        await service.EvaluateAsync(item);
        await service.SetScoreAsync(item, 10, 90);
        await service.MarkFailedAsync(
            (await service.EvaluateAsync(item)).DownloadAttemptId, "err", "Error");

        var second = await service.EvaluateAsync(item);
        var snapshot = await service.GetSnapshotAsync(second.DownloadAttemptId);

        Assert.NotNull(snapshot);
        Assert.NotEqual(10, snapshot.VisibleScore);
    }

    #endregion
}
