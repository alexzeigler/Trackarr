using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>
///     Regression guards for C2: downloads that are waiting (queued, paused, delay profile, import) must not be
///     scored, so they can never reach removal and blocklisting just for showing no progress.
/// </summary>
public class HealthExemptionTests
{
    [Theory]
    [InlineData("queued", null, 1000)]
    [InlineData("Paused", null, 1000)]
    [InlineData("delay", null, 1000)]
    [InlineData("downloadClientUnavailable", null, 1000)]
    [InlineData("completed", "importPending", 0)]
    [InlineData("completed", "importBlocked", 0)]
    [InlineData("completed", "importing", 0)]
    public void WaitingStates_AreExempt(string status, string? trackedState, long sizeLeft)
    {
        var item = new DownloadItem
        {
            Status = status, TrackedDownloadState = trackedState, Size = 1000, SizeLeft = sizeLeft
        };

        Assert.NotNull(HealthEvaluator.GetScoringExemption(item));
    }

    [Theory]
    [InlineData("downloading", "downloading", 500)]
    [InlineData("warning", "downloading", 500)]
    [InlineData("failed", "failedPending", 500)]
    [InlineData("completed", "importBlocked", 100)] // bytes still left: not a finished download
    public void ActiveOrFailedStates_AreScored(string status, string trackedState, long sizeLeft)
    {
        var item = new DownloadItem
        {
            Status = status, TrackedDownloadState = trackedState, Size = 1000, SizeLeft = sizeLeft
        };

        Assert.Null(HealthEvaluator.GetScoringExemption(item));
    }

    [Fact]
    public void DownloadClientUnavailable_DetectedOnAnyItem()
    {
        List<DownloadItem> queue =
        [
            new() { Status = "downloading" },
            new() { Status = "downloadClientUnavailable" }
        ];

        Assert.True(HealthEvaluator.IsDownloadClientUnavailable(queue));
        Assert.False(HealthEvaluator.IsDownloadClientUnavailable([new DownloadItem { Status = "downloading" }]));
    }

    [Fact]
    public async Task PausedDownload_IsNeverScoredOrRemovable()
    {
        using var factory = new SqliteInMemoryFactory();
        int serverId;
        await using (var db = factory.CreateDbContext())
        {
            var server = new MediaServer
            {
                Name = "Test Radarr", ServiceType = ServiceType.Radarr, Host = "localhost", Port = 7878
            };
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();
            serverId = server.Id;
        }

        var service = new HealthService(factory,
            new SettingsService(factory, NullLogger<SettingsService>.Instance, new DataChangeBus()),
            new DataChangeBus());

        var paused = new DownloadItem
        {
            ServerId = serverId,
            RemoteId = 55,
            MovieId = 900,
            DownloadId = "dl-paused",
            Title = "Paused Movie",
            Size = 2000,
            SizeLeft = 2000,
            Status = "paused",
            Protocol = "torrent"
        };

        // Well past the ~10 scans that used to push a no-progress item to removal.
        for (var i = 0; i < 15; i++)
        {
            var decision = await service.EvaluateAsync(paused);
            Assert.False(decision.ShouldRemove);
            Assert.Equal("waiting", decision.ReasonKey);
        }

        await using var check = factory.CreateDbContext();
        Assert.Equal(1, await check.DownloadAttempts.CountAsync());
        Assert.Equal(0, await check.HealthEvents.CountAsync());

        var snapshot = await check.HealthSnapshots.SingleAsync();
        Assert.Equal(0, snapshot.ConsecutiveBadScans);
        Assert.Null(snapshot.LastObservationAt);
        Assert.False(snapshot.IsEligibleForRemoval);
    }
}
