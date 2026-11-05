using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Services.Media;

namespace Trackarr.Tests;

/// <summary>
///     Usenet items waiting their turn show as Queued and aren't scored; downloads that have only ever waited
///     have no score yet ("–") and stay out of the averages.
/// </summary>
public class QueueDisplayTests
{
    [Fact]
    public void Usenet_NothingDownloadedYet_IsQueuedAndExempt()
    {
        var item = new DownloadItem { Protocol = "usenet", Status = "downloading", Size = 1000, SizeLeft = 1000 };

        Assert.True(HealthEvaluator.IsWaitingInUsenetQueue(item));
        Assert.Equal("queued", HealthEvaluator.GetDisplayStatus(item));
        Assert.NotNull(HealthEvaluator.GetScoringExemption(item));
    }

    [Theory]
    [InlineData("usenet", "downloading", 400)] // bytes are moving: really downloading
    [InlineData("torrent", "downloading", 1000)] // torrents download in parallel, so 0% can mean stalled
    [InlineData("usenet", "warning", 1000)]
    public void OtherItems_KeepTheirStatusAndAreScored(string protocol, string status, long sizeLeft)
    {
        var item = new DownloadItem { Protocol = protocol, Status = status, Size = 1000, SizeLeft = sizeLeft };

        Assert.False(HealthEvaluator.IsWaitingInUsenetQueue(item));
        Assert.Equal(status, HealthEvaluator.GetDisplayStatus(item));
        Assert.Null(HealthEvaluator.GetScoringExemption(item));
    }

    [Fact]
    public void Usenet_UnknownSize_IsNotTreatedAsQueued()
    {
        var item = new DownloadItem { Protocol = "usenet", Status = "downloading", Size = 0, SizeLeft = 0 };

        Assert.False(HealthEvaluator.IsWaitingInUsenetQueue(item));
    }

    [Fact]
    public void HasBeenScored_OnlyAfterARealScanOrManualScore()
    {
        Assert.False(HealthEvaluator.HasBeenScored(null));
        Assert.False(HealthEvaluator.HasBeenScored(new HealthSnapshot())); // only ever seen waiting
        Assert.True(HealthEvaluator.HasBeenScored(new HealthSnapshot { LastObservationAt = DateTime.UtcNow }));
        Assert.True(HealthEvaluator.HasBeenScored(new HealthSnapshot { LastReasonKey = "manual-override" }));
    }

    [Fact]
    public void Stats_LeaveUnscoredDownloadsOutOfTheAverage()
    {
        using var factory = new SqliteInMemoryFactory();
        var stats = new QueueStatsService(factory);
        var downloads = new List<DownloadItem> { new() { RemoteId = 1 }, new() { RemoteId = 2 } };
        var snapshots = new Dictionary<string, HealthSnapshot>
        {
            ["1"] = new() { VisibleScore = 40, LastObservationAt = DateTime.UtcNow },
            ["2"] = new() { VisibleScore = 70 } // starting score of a download that has only waited
        };

        var result = stats.BuildStats(downloads, snapshots, new Dictionary<string, MediaHealthSummary>(),
            d => d.RemoteId.ToString());

        Assert.Equal(40, result.Stats.AvgHealthScore);
        Assert.Equal(1, result.ScoreDistribution.Sum(b => b.Count));
    }
}
