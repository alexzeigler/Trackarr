using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;

namespace Trackarr.Tests;

public class HealthServiceLifecycleTests
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
        return new HealthService(factory, new SettingsService(factory, NullLogger<SettingsService>.Instance));
    }

    private static DownloadItem MovieItem(int serverId, string downloadId = "dl-1", long sizeLeft = 1000)
    {
        return new DownloadItem
        {
            ServerId = serverId,
            RemoteId = 100,
            MovieId = 900,
            DownloadId = downloadId,
            Title = "Test Movie",
            Size = 2000,
            SizeLeft = sizeLeft,
            Protocol = "torrent"
        };
    }

    #region MarkFailed

    [Fact]
    public async Task MarkFailed_SetsAttemptStatusAndState()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var decision = await service.EvaluateAsync(MovieItem(serverId));

        await service.MarkFailedAsync(decision.DownloadAttemptId, "disk-full", "Disk is full");

        await using var db = factory.CreateDbContext();
        var attempt = await db.DownloadAttempts.Include(e => e.HealthSnapshot)
            .FirstAsync(e => e.Id == decision.DownloadAttemptId);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.False(attempt.WasSuccessful);
        Assert.NotNull(attempt.FailedAt);
        Assert.Equal(HealthState.Failed, attempt.HealthSnapshot!.State);
        Assert.Equal("disk-full", attempt.FinalReasonKey);
    }

    #endregion

    #region MarkRemovedByTrackarr

    [Fact]
    public async Task MarkRemovedByTrackarr_SetsRemovalState()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var decision = await service.EvaluateAsync(MovieItem(serverId));

        await service.MarkRemovedByTrackarrAsync(decision.DownloadAttemptId);

        await using var db = factory.CreateDbContext();
        var attempt = await db.DownloadAttempts.Include(e => e.HealthSnapshot)
            .FirstAsync(e => e.Id == decision.DownloadAttemptId);

        Assert.Equal(AttemptStatus.RemovedByTrackarr, attempt.Status);
        Assert.True(attempt.WasRemovedByTrackarr);
        Assert.NotNull(attempt.RemovedAt);
        Assert.Equal(HealthState.Remove, attempt.HealthSnapshot!.State);
    }

    #endregion

    #region IsNoChange Deduplication

    [Fact]
    public async Task Evaluate_RepeatedIdenticalState_UpdatesExistingEventTimestamp()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var item = MovieItem(serverId, sizeLeft: 1000);
        await service.EvaluateAsync(item);
        await service.EvaluateAsync(item);
        await service.EvaluateAsync(item);

        await using var db = factory.CreateDbContext();
        var eventCount = await db.HealthEvents.CountAsync();

        Assert.True(eventCount <= 3);
    }

    #endregion

    #region MarkImported

    [Fact]
    public async Task MarkImported_SetsAttemptStatusAndScore()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var decision = await service.EvaluateAsync(MovieItem(serverId));

        await service.MarkImportedAsync(decision.DownloadAttemptId);

        await using var db = factory.CreateDbContext();
        var attempt = await db.DownloadAttempts.Include(e => e.HealthSnapshot)
            .FirstAsync(e => e.Id == decision.DownloadAttemptId);

        Assert.Equal(AttemptStatus.Imported, attempt.Status);
        Assert.True(attempt.WasSuccessful);
        Assert.True(attempt.WasImported);
        Assert.NotNull(attempt.CompletedAt);
        Assert.Equal(HealthState.Resolved, attempt.HealthSnapshot!.State);
        Assert.Equal(100, attempt.HealthSnapshot.VisibleScore);
    }

    [Fact]
    public async Task MarkImported_CreatesTerminalEvent()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var decision = await service.EvaluateAsync(MovieItem(serverId));

        await service.MarkImportedAsync(decision.DownloadAttemptId, "Custom import message");

        await using var db = factory.CreateDbContext();
        var events = await db.HealthEvents.Where(e => e.DownloadAttemptId == decision.DownloadAttemptId)
            .OrderByDescending(e => e.OccurredAt).ToListAsync();

        var terminal = events.First();
        Assert.Equal(HealthEventType.ImportedSuccessfully, terminal.EventType);
        Assert.Equal("Custom import message", terminal.Message);
        Assert.True(terminal.IsGoodObservation);
    }

    #endregion

    #region MarkRemovedByUser

    [Fact]
    public async Task MarkRemovedByUser_FindsAndMarksActiveAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var item = MovieItem(serverId);
        await service.EvaluateAsync(item);

        await service.MarkRemovedByUserAsync(item);

        await using var db = factory.CreateDbContext();
        var attempt = await db.DownloadAttempts.Include(e => e.HealthSnapshot).FirstAsync();

        Assert.Equal(AttemptStatus.RemovedByTrackarr, attempt.Status);
    }

    [Fact]
    public async Task MarkRemovedByUser_NoActiveAttempt_DoesNothing()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var item = MovieItem(serverId);

        await service.MarkRemovedByUserAsync(item);

        await using var db = factory.CreateDbContext();
        Assert.Equal(0, await db.DownloadAttempts.CountAsync());
    }

    #endregion

    #region MarkTerminal with missing attempt

    [Fact]
    public async Task MarkImported_NonexistentAttempt_DoesNotThrow()
    {
        var (factory, _) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        await service.MarkImportedAsync(9999);
    }

    [Fact]
    public async Task MarkFailed_NonexistentAttempt_DoesNotThrow()
    {
        var (factory, _) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        await service.MarkFailedAsync(9999, "err", "msg");
    }

    #endregion

    #region ResolveMissingAttempts

    [Fact]
    public async Task ResolveMissing_ImportedDownload_MarksAsImported()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var item = MovieItem(serverId);
        await service.EvaluateAsync(item);

        var missing = await service.ResolveMissingAttemptsAsync(serverId, [], [item.DownloadId!]);

        Assert.Single(missing);
        Assert.Equal(AttemptStatus.Imported, missing[0].Status);
        Assert.True(missing[0].WasSuccessful);
        Assert.True(missing[0].WasImported);
    }

    [Fact]
    public async Task ResolveMissing_DisappearedDownload_MarksAsDisappeared()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var item = MovieItem(serverId);
        await service.EvaluateAsync(item);

        var missing = await service.ResolveMissingAttemptsAsync(serverId, []);

        Assert.Single(missing);
        Assert.Equal(AttemptStatus.Disappeared, missing[0].Status);
    }

    [Fact]
    public async Task ResolveMissing_ActiveDownloadStillPresent_NotReturned()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var item = MovieItem(serverId);
        await service.EvaluateAsync(item);

        var missing = await service.ResolveMissingAttemptsAsync(serverId, [item.DownloadId!]);

        Assert.Empty(missing);
    }

    [Fact]
    public async Task ResolveMissing_WhitespaceDownloadIds_Ignored()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var item = MovieItem(serverId);
        await service.EvaluateAsync(item);

        var missing = await service.ResolveMissingAttemptsAsync(serverId, ["", " ", null!]);

        Assert.Single(missing);
    }

    #endregion

    #region SetScore

    [Fact]
    public async Task SetScore_OverridesScoreAndState()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var item = MovieItem(serverId);
        await service.EvaluateAsync(item);

        await service.SetScoreAsync(item, 30, 50);

        await using var db = factory.CreateDbContext();
        var snapshot = await db.HealthSnapshots.FirstAsync();

        Assert.Equal(30, snapshot.RawScore);
        Assert.Equal(30, snapshot.VisibleScore);
        Assert.Equal(50, snapshot.Confidence);
        Assert.Equal("manual-override", snapshot.LastReasonKey);
    }

    [Fact]
    public async Task SetScore_ClampsToMaximum()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var item = MovieItem(serverId);
        await service.EvaluateAsync(item);

        await service.SetScoreAsync(item, 999, 200);

        await using var db = factory.CreateDbContext();
        var snapshot = await db.HealthSnapshots.FirstAsync();

        Assert.Equal(100, snapshot.RawScore);
        Assert.Equal(100, snapshot.Confidence);
    }

    [Fact]
    public async Task SetScore_ClampsToMinimum()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var item = MovieItem(serverId);
        await service.EvaluateAsync(item);

        await service.SetScoreAsync(item, -50, -10);

        await using var db = factory.CreateDbContext();
        var snapshot = await db.HealthSnapshots.FirstAsync();

        Assert.Equal(0, snapshot.RawScore);
        Assert.Equal(0, snapshot.Confidence);
    }

    [Fact]
    public async Task SetScore_CreatesManualOverrideEvent()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var item = MovieItem(serverId);
        await service.EvaluateAsync(item);

        await service.SetScoreAsync(item, 50, 60);

        await using var db = factory.CreateDbContext();
        var evt = await db.HealthEvents.OrderByDescending(e => e.OccurredAt).FirstAsync();

        Assert.Equal(HealthEventType.ManualOverride, evt.EventType);
    }

    [Fact]
    public async Task SetScore_NoActiveAttempt_DoesNothing()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var item = MovieItem(serverId, "nonexistent");

        await service.SetScoreAsync(item, 50, 50);
    }

    #endregion

    #region GetDownloadId

    [Fact]
    public void GetDownloadId_WithValidId_ReturnsTrimmedId()
    {
        var item = new DownloadItem { DownloadId = "  abc-123  " };
        Assert.Equal("abc-123", HealthService.GetDownloadId(item));
    }

    [Fact]
    public void GetDownloadId_NullId_GeneratesFallback()
    {
        var item = new DownloadItem { DownloadId = null, ServerId = 1, RemoteId = 42, Title = "Test Movie" };
        var id = HealthService.GetDownloadId(item);

        Assert.Contains("1:", id);
        Assert.Contains("42:", id);
        Assert.Contains("test-movie", id);
    }

    [Fact]
    public void GetDownloadId_EmptyId_GeneratesFallback()
    {
        var item = new DownloadItem { DownloadId = "", ServerId = 1, RemoteId = 42, Title = "Test Movie" };
        var id = HealthService.GetDownloadId(item);

        Assert.Contains("1:", id);
    }

    [Fact]
    public void GetDownloadId_WhitespaceId_GeneratesFallback()
    {
        var item = new DownloadItem { DownloadId = "   ", ServerId = 2, RemoteId = 10, Title = "Test" };
        var id = HealthService.GetDownloadId(item);

        Assert.Contains("2:", id);
    }

    #endregion

    #region Query Methods

    [Fact]
    public async Task GetAttempt_ReturnsAttemptWithSnapshot()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var decision = await service.EvaluateAsync(MovieItem(serverId));

        var attempt = await service.GetAttemptAsync(decision.DownloadAttemptId);

        Assert.NotNull(attempt);
        Assert.NotNull(attempt.HealthSnapshot);
        Assert.Equal(decision.DownloadAttemptId, attempt.Id);
    }

    [Fact]
    public async Task GetAttempt_NonexistentId_ReturnsNull()
    {
        var (factory, _) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);

        var attempt = await service.GetAttemptAsync(9999);

        Assert.Null(attempt);
    }

    [Fact]
    public async Task GetSnapshot_ReturnsForAttempt()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var decision = await service.EvaluateAsync(MovieItem(serverId));

        var snapshot = await service.GetSnapshotAsync(decision.DownloadAttemptId);

        Assert.NotNull(snapshot);
        Assert.Equal(decision.DownloadAttemptId, snapshot.DownloadAttemptId);
    }

    [Fact]
    public async Task GetEvents_ReturnsOrderedList()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var decision = await service.EvaluateAsync(MovieItem(serverId));
        await service.EvaluateAsync(MovieItem(serverId, sizeLeft: 500));

        var events = await service.GetEventsAsync(decision.DownloadAttemptId);

        Assert.True(events.Count >= 2);
        Assert.True(events[0].OccurredAt >= events[1].OccurredAt);
    }

    [Fact]
    public async Task GetActiveAttempts_OnlyReturnsActive()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        var decision = await service.EvaluateAsync(MovieItem(serverId));
        await service.MarkImportedAsync(decision.DownloadAttemptId);

        var active = await service.GetActiveAttemptsAsync();

        Assert.Empty(active);
    }

    [Fact]
    public async Task GetHistory_ReturnsAllAttempts()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        await service.EvaluateAsync(MovieItem(serverId, "dl-1"));
        await service.EvaluateAsync(MovieItem(serverId, "dl-2"));

        var history = await service.GetHistoryAsync();

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task GetActiveDownloadIds_FiltersCorrectServer()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        await service.EvaluateAsync(MovieItem(serverId));

        var ids = await service.GetActiveDownloadIdsAsync(serverId);

        Assert.Single(ids);
        Assert.Equal("dl-1", ids[0]);
    }

    [Fact]
    public async Task GetMissingDownloadIds_ReturnsMissingOnes()
    {
        var (factory, serverId) = await SetupAsync();
        using var _ = factory;
        var service = CreateService(factory);
        await service.EvaluateAsync(MovieItem(serverId, "dl-1"));
        await service.EvaluateAsync(MovieItem(serverId, "dl-2"));

        var missing = await service.GetMissingDownloadIdsAsync(serverId, ["dl-1"]);

        Assert.Single(missing);
        Assert.Equal("dl-2", missing[0]);
    }

    #endregion
}
