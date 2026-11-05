using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Services.Media;

namespace Trackarr.Tests;

public class QueueStatsServiceTests : IDisposable
{
    // A snapshot only counts toward averages once it has been scored (not just seen waiting in a queue).
    private static readonly DateTime Seen = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteInMemoryFactory _factory = new();
    private readonly QueueStatsService _sut;

    public QueueStatsServiceTests()
    {
        _sut = new QueueStatsService(_factory);
    }

    public void Dispose() => _factory.Dispose();

    #region BuildStats — basic counts

    [Fact]
    public void BuildStats_EmptyDownloads_ReturnsZeroCounts()
    {
        var result = _sut.BuildStats([], new(), new(), d => "");

        Assert.Equal(0, result.Stats.TotalInQueue);
        Assert.Equal(0, result.Stats.Downloading);
        Assert.Equal(0, result.Stats.AtRisk);
        Assert.Equal(0, result.Stats.AvgHealthScore);
        Assert.Empty(result.StatusBreakdown);
    }

    [Fact]
    public void BuildStats_TotalInQueue_EqualsDownloadCount()
    {
        var downloads = new List<DownloadItem>
        {
            MakeDownload(status: "queued"), MakeDownload(status: "downloading"), MakeDownload(status: "paused")
        };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        Assert.Equal(3, result.Stats.TotalInQueue);
    }

    #endregion

    #region BuildStats — Downloading detection

    [Fact]
    public void BuildStats_Downloading_CountsByPercentBetweenZeroAndHundred()
    {
        var downloads = new List<DownloadItem>
        {
            MakeDownload(size: 100, sizeLeft: 50), // 50% -> downloading
            MakeDownload(size: 100, sizeLeft: 0), // 100% -> not downloading
            MakeDownload(size: 100, sizeLeft: 100) // 0% -> not downloading
        };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        Assert.Equal(1, result.Stats.Downloading);
    }

    [Fact]
    public void BuildStats_Downloading_CountsByStatusContainingDownload()
    {
        var downloads = new List<DownloadItem>
        {
            MakeDownload(status: "Downloading", size: 100, sizeLeft: 100), // 0% but status matches
            MakeDownload(status: "queued", size: 100, sizeLeft: 100) // no match
        };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        Assert.Equal(1, result.Stats.Downloading);
    }

    [Fact]
    public void BuildStats_Downloading_PercentAndStatusBothCount()
    {
        var downloads = new List<DownloadItem>
        {
            MakeDownload(status: "downloading", size: 100, sizeLeft: 50), // both match, still 1
            MakeDownload(status: "downloading", size: 100, sizeLeft: 100) // status match only
        };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        Assert.Equal(2, result.Stats.Downloading);
    }

    #endregion

    #region BuildStats — AtRisk detection

    [Fact]
    public void BuildStats_AtRisk_CountsLowScoreSnapshots()
    {
        var downloads = new List<DownloadItem> { MakeDownload(), MakeDownload() };

        var snapshots = new Dictionary<string, HealthSnapshot>
        {
            [Key(downloads[0])] = new() { VisibleScore = 30, LastObservationAt = Seen }, // <= 39 -> at risk
            [Key(downloads[1])] = new() { VisibleScore = 80, LastObservationAt = Seen } // fine
        };

        var result = _sut.BuildStats(downloads, snapshots, new(), d => Key(d));

        Assert.Equal(1, result.Stats.AtRisk);
    }

    [Fact]
    public void BuildStats_AtRisk_CountsEligibleForRemoval()
    {
        var downloads = new List<DownloadItem> { MakeDownload() };

        var snapshots = new Dictionary<string, HealthSnapshot>
        {
            [Key(downloads[0])] = new() { VisibleScore = 90, IsEligibleForRemoval = true, LastObservationAt = Seen }
        };

        var result = _sut.BuildStats(downloads, snapshots, new(), d => Key(d));

        Assert.Equal(1, result.Stats.AtRisk);
    }

    [Fact]
    public void BuildStats_AtRisk_CountsProblematicStatuses()
    {
        var statuses = new[] { "stuck", "stalled", "failed", "error", "warning" };
        var downloads = statuses.Select(s => MakeDownload(status: s)).ToList();

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        Assert.Equal(5, result.Stats.AtRisk);
    }

    [Fact]
    public void BuildStats_AtRisk_HealthyStatusNotCounted()
    {
        var downloads = new List<DownloadItem> { MakeDownload(status: "downloading"), MakeDownload(status: "queued") };

        var snapshots = new Dictionary<string, HealthSnapshot>
        {
            [Key(downloads[0])] = new() { VisibleScore = 95, LastObservationAt = Seen },
            [Key(downloads[1])] = new() { VisibleScore = 85, LastObservationAt = Seen }
        };

        var result = _sut.BuildStats(downloads, snapshots, new(), d => Key(d));

        Assert.Equal(0, result.Stats.AtRisk);
    }

    #endregion

    #region BuildStats — AvgHealthScore

    [Fact]
    public void BuildStats_AvgHealthScore_AveragesVisibleScores()
    {
        var downloads = new List<DownloadItem> { MakeDownload(), MakeDownload(), MakeDownload() };

        var snapshots = new Dictionary<string, HealthSnapshot>
        {
            [Key(downloads[0])] = new() { VisibleScore = 60, LastObservationAt = Seen },
            [Key(downloads[1])] = new() { VisibleScore = 80, LastObservationAt = Seen },
            [Key(downloads[2])] = new() { VisibleScore = 100, LastObservationAt = Seen }
        };

        var result = _sut.BuildStats(downloads, snapshots, new(), d => Key(d));

        Assert.Equal(80, result.Stats.AvgHealthScore); // (60+80+100)/3 = 80
    }

    [Fact]
    public void BuildStats_AvgHealthScore_ZeroWhenNoSnapshots()
    {
        var downloads = new List<DownloadItem> { MakeDownload() };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        Assert.Equal(0, result.Stats.AvgHealthScore);
    }

    [Fact]
    public void BuildStats_AvgHealthScore_IgnoresDownloadsWithoutSnapshot()
    {
        var downloads = new List<DownloadItem> { MakeDownload(), MakeDownload() };

        // Only one download has a snapshot
        var snapshots = new Dictionary<string, HealthSnapshot>
        {
            [Key(downloads[0])] = new() { VisibleScore = 50, LastObservationAt = Seen }
        };

        var result = _sut.BuildStats(downloads, snapshots, new(), d => Key(d));

        Assert.Equal(50, result.Stats.AvgHealthScore);
    }

    #endregion

    #region BuildStats — StatusBreakdown

    [Fact]
    public void BuildStats_StatusBreakdown_GroupsByBucket()
    {
        var downloads = new List<DownloadItem>
        {
            MakeDownload(status: "downloading"),
            MakeDownload(status: "Downloading fast"),
            MakeDownload(status: "failed")
        };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        var downloading = result.StatusBreakdown.SingleOrDefault(b => b.Label == "Downloading");
        Assert.NotNull(downloading);
        Assert.Equal(2, downloading.Count);

        var failed = result.StatusBreakdown.SingleOrDefault(b => b.Label == "Failed");
        Assert.NotNull(failed);
        Assert.Equal(1, failed.Count);
    }

    [Fact]
    public void BuildStats_StatusBreakdown_CalculatesPercent()
    {
        var downloads = new List<DownloadItem>
        {
            MakeDownload(status: "downloading"),
            MakeDownload(status: "downloading"),
            MakeDownload(status: "downloading"),
            MakeDownload(status: "failed")
        };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        var downloading = result.StatusBreakdown.Single(b => b.Label == "Downloading");
        Assert.Equal(75.0m, downloading.Percent);
    }

    [Fact]
    public void BuildStats_StatusBreakdown_MapsStatusBucketsCorrectly()
    {
        var downloads = new List<DownloadItem>
        {
            MakeDownload(status: "importing"),
            MakeDownload(status: "stalled"),
            MakeDownload(status: "error occurred"),
            MakeDownload(status: "waiting"),
            MakeDownload(status: "paused"),
            MakeDownload(status: "metadata check")
        };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        var labels = result.StatusBreakdown.Select(b => b.Label).ToList();
        Assert.Contains("Importing", labels);
        Assert.Contains("Stalled", labels);
        Assert.Contains("Failed", labels); // "error" maps to Failed bucket
        Assert.Contains("Queued", labels); // "waiting" maps to Queued
        Assert.Contains("Paused", labels);
        Assert.Contains("Fetching Metadata", labels);
    }

    [Fact]
    public void BuildStats_StatusBreakdown_NullStatusMapsToUnknown()
    {
        var downloads = new List<DownloadItem> { MakeDownload(status: null) };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        var unknown = result.StatusBreakdown.SingleOrDefault(b => b.Label == "Unknown");
        Assert.NotNull(unknown);
        Assert.Equal(1, unknown.Count);
    }

    #endregion

    #region BuildStats — ScoreDistribution

    [Fact]
    public void BuildStats_ScoreDistribution_BucketsCorrectly()
    {
        var downloads = new List<DownloadItem>
        {
            MakeDownload(),
            MakeDownload(),
            MakeDownload(),
            MakeDownload(),
            MakeDownload()
        };

        var snapshots = new Dictionary<string, HealthSnapshot>
        {
            [Key(downloads[0])] = new() { VisibleScore = 20, LastObservationAt = Seen }, // Critical
            [Key(downloads[1])] = new() { VisibleScore = 50, LastObservationAt = Seen }, // Poor
            [Key(downloads[2])] = new() { VisibleScore = 70, LastObservationAt = Seen }, // Fair
            [Key(downloads[3])] = new() { VisibleScore = 85, LastObservationAt = Seen }, // Good
            [Key(downloads[4])] = new() { VisibleScore = 95, LastObservationAt = Seen } // Healthy
        };

        var result = _sut.BuildStats(downloads, snapshots, new(), d => Key(d));

        Assert.Equal(5, result.ScoreDistribution.Count);
        Assert.Equal(1, result.ScoreDistribution.Single(b => b.Label == "Critical (0-39)").Count);
        Assert.Equal(1, result.ScoreDistribution.Single(b => b.Label == "Poor (40-59)").Count);
        Assert.Equal(1, result.ScoreDistribution.Single(b => b.Label == "Fair (60-79)").Count);
        Assert.Equal(1, result.ScoreDistribution.Single(b => b.Label == "Good (80-89)").Count);
        Assert.Equal(1, result.ScoreDistribution.Single(b => b.Label == "Healthy (90+)").Count);
    }

    [Fact]
    public void BuildStats_ScoreDistribution_BoundaryValues()
    {
        var downloads = new List<DownloadItem>
        {
            MakeDownload(),
            MakeDownload(),
            MakeDownload(),
            MakeDownload(),
            MakeDownload(),
            MakeDownload(),
            MakeDownload(),
            MakeDownload()
        };

        var snapshots = new Dictionary<string, HealthSnapshot>
        {
            [Key(downloads[0])] = new() { VisibleScore = 0, LastObservationAt = Seen }, // Critical
            [Key(downloads[1])] = new() { VisibleScore = 39, LastObservationAt = Seen }, // Critical
            [Key(downloads[2])] = new() { VisibleScore = 40, LastObservationAt = Seen }, // Poor
            [Key(downloads[3])] = new() { VisibleScore = 59, LastObservationAt = Seen }, // Poor
            [Key(downloads[4])] = new() { VisibleScore = 60, LastObservationAt = Seen }, // Fair
            [Key(downloads[5])] = new() { VisibleScore = 79, LastObservationAt = Seen }, // Fair
            [Key(downloads[6])] = new() { VisibleScore = 80, LastObservationAt = Seen }, // Good
            [Key(downloads[7])] = new() { VisibleScore = 90, LastObservationAt = Seen } // Healthy
        };

        var result = _sut.BuildStats(downloads, snapshots, new(), d => Key(d));

        Assert.Equal(2, result.ScoreDistribution.Single(b => b.Label == "Critical (0-39)").Count);
        Assert.Equal(2, result.ScoreDistribution.Single(b => b.Label == "Poor (40-59)").Count);
        Assert.Equal(2, result.ScoreDistribution.Single(b => b.Label == "Fair (60-79)").Count);
        Assert.Equal(1, result.ScoreDistribution.Single(b => b.Label == "Good (80-89)").Count);
        Assert.Equal(1, result.ScoreDistribution.Single(b => b.Label == "Healthy (90+)").Count);
    }

    [Fact]
    public void BuildStats_ScoreDistribution_EmptyWhenNoSnapshots()
    {
        var downloads = new List<DownloadItem> { MakeDownload() };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        Assert.All(result.ScoreDistribution, b => Assert.Equal(0, b.Count));
    }

    #endregion

    #region BuildStats — DataRemaining and OldestInQueue

    [Fact]
    public void BuildStats_DataRemaining_SumsSizeLeft()
    {
        var downloads = new List<DownloadItem>
        {
            MakeDownload(size: 1000, sizeLeft: 500), MakeDownload(size: 2000, sizeLeft: 800)
        };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        Assert.Equal(1300, result.Stats.DataRemaining);
    }

    [Fact]
    public void BuildStats_OldestInQueue_ComputedFromAddedDates()
    {
        var downloads = new List<DownloadItem>
        {
            MakeDownload(added: DateTime.UtcNow.AddHours(-5)), MakeDownload(added: DateTime.UtcNow.AddHours(-2))
        };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        Assert.NotNull(result.Stats.OldestInQueue);
        Assert.True(result.Stats.OldestInQueue.Value.TotalHours >= 4.9);
    }

    [Fact]
    public void BuildStats_OldestInQueue_NullWhenNoAddedDates()
    {
        var downloads = new List<DownloadItem> { MakeDownload(added: null) };

        var result = _sut.BuildStats(downloads, new(), new(), d => Key(d));

        Assert.Null(result.Stats.OldestInQueue);
    }

    #endregion

    #region Helpers

    private static int _nextId;

    private static DownloadItem MakeDownload(string? status = null, long size = 0, long sizeLeft = 0,
        DateTime? added = null)
    {
        var id = ++_nextId;
        return new DownloadItem
        {
            ServerId = 1,
            RemoteId = id,
            Title = $"Test Download {id}",
            Status = status,
            Size = size,
            SizeLeft = sizeLeft,
            Added = added
        };
    }

    private static string Key(DownloadItem d) => $"{d.ServerId}_{d.RemoteId}";

    #endregion
}
