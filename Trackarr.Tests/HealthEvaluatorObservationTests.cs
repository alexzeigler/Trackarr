using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Services.Media;

namespace Trackarr.Tests;

public class BuildObservationTests
{
    private static DownloadAttempt NewAttempt()
    {
        return new DownloadAttempt
        {
            Id = 1,
            DownloadId = "abc",
            Title = "Test Download",
            Status = AttemptStatus.Active,
            FirstSeenAt = DateTime.UtcNow.AddHours(-1)
        };
    }

    private static HealthSnapshot FreshSnapshot()
    {
        return new HealthSnapshot();
    }

    private static HealthSnapshot ObservedSnapshot(decimal? lastProgress = 50m, long? lastSizeLeft = 500)
    {
        return new HealthSnapshot
        {
            LastObservationAt = DateTime.UtcNow.AddMinutes(-5),
            LastProgress = lastProgress,
            LastSizeLeft = lastSizeLeft
        };
    }

    #region First Observation

    [Fact]
    public void FirstObservation_HasNoPreviousProgress()
    {
        var item = new DownloadItem { Size = 1000, SizeLeft = 500 };
        var observation = HealthEvaluator.BuildObservation(item, NewAttempt(), FreshSnapshot(), DateTime.UtcNow);

        Assert.Equal(50m, observation.Progress);
        Assert.False(observation.HasProgressIncrease);
        Assert.False(observation.HasSizeLeftDecrease);
    }

    #endregion

    #region Progress Detection

    [Fact]
    public void ProgressIncreased_WhenPercentRises()
    {
        var snapshot = ObservedSnapshot(30m, 700);
        var item = new DownloadItem { Size = 1000, SizeLeft = 400 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.HasProgressIncrease);
        Assert.True(obs.HasSizeLeftDecrease);
        Assert.True(obs.IsGoodObservation);
        Assert.False(obs.IsBadObservation);
    }

    [Fact]
    public void SizeLeftDecreased_ButProgressSame_StillActivelyProgressing()
    {
        var snapshot = ObservedSnapshot(50m, 600);
        var item = new DownloadItem { Size = 1000, SizeLeft = 500 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.HasSizeLeftDecrease);
        Assert.True(obs.HasPositiveSpeed);
    }

    [Fact]
    public void NoProgressChange_WhenSameSizeLeft()
    {
        var snapshot = ObservedSnapshot(50m, 500);
        var item = new DownloadItem { Size = 1000, SizeLeft = 500 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.False(obs.HasProgressIncrease);
        Assert.False(obs.HasSizeLeftDecrease);
        Assert.True(obs.HasNoProgress);
    }

    [Fact]
    public void SizeLeftIncreased_NotProgress()
    {
        var snapshot = ObservedSnapshot(50m, 400);
        var item = new DownloadItem { Size = 1000, SizeLeft = 500 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.False(obs.HasSizeLeftDecrease);
    }

    #endregion

    #region Stall Detection

    [Fact]
    public void Stalled_DetectedInStatus()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, Status = "Stalled" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsStalled);
        Assert.True(obs.IsBadObservation);
    }

    [Fact]
    public void Stalled_DetectedInTrackedDownloadStatus()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, TrackedDownloadStatus = "stalled" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsStalled);
    }

    [Fact]
    public void Stalled_DetectedInTrackedDownloadState()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, TrackedDownloadState = "importBlocked_stuck" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsStalled);
    }

    [Fact]
    public void Stalled_DetectedInErrorMessage()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, ErrorMessage = "The download is stalled" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsStalled);
    }

    [Fact]
    public void Stuck_AlsoDetectedAsStalled()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, Status = "stuck" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsStalled);
    }

    [Fact]
    public void StalledNoConnections_DetectedByNoConnections()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem
        {
            Size = 1000,
            SizeLeft = 500,
            Status = "stalled",
            ErrorMessage = "The download is stalled with no connections"
        };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsStalledNoConnections);
        Assert.True(obs.IsStalled);
    }

    [Fact]
    public void StalledNoConnections_DetectedByNoPeers()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem
        {
            Size = 1000, SizeLeft = 500, Status = "stalled", ErrorMessage = "Stalled: no peers available"
        };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsStalledNoConnections);
    }

    [Fact]
    public void StalledNoConnections_DetectedByZeroPeers()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem
        {
            Size = 1000, SizeLeft = 500, Status = "stalled", ErrorMessage = "Download stalled with 0 peers"
        };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsStalledNoConnections);
    }

    [Fact]
    public void NotStalledNoConnections_WhenNotStalledFirst()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, ErrorMessage = "no connections" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.False(obs.IsStalledNoConnections);
    }

    [Fact]
    public void ActivelyProgressing_SuppressesStallDetection()
    {
        var snapshot = ObservedSnapshot(30m, 700);
        var item = new DownloadItem
        {
            Size = 1000, SizeLeft = 400, Status = "stalled", ErrorMessage = "stalled with no connections"
        };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.False(obs.IsStalled);
        Assert.False(obs.IsStalledNoConnections);
        Assert.True(obs.HasProgressIncrease);
    }

    #endregion

    #region Error Detection

    [Fact]
    public void Error_Detected()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, ErrorMessage = "Import failed: disk full" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.HasError);
        Assert.True(obs.IsBadObservation);
    }

    [Fact]
    public void RepeatedError_WhenSameNormalizedKey()
    {
        var snapshot = ObservedSnapshot();
        snapshot.LastErrorKey = "import-failed";
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, ErrorMessage = "import failed again" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsRepeatedError);
    }

    [Fact]
    public void ErrorCleared_WhenPreviousErrorGone()
    {
        var snapshot = ObservedSnapshot();
        snapshot.LastErrorMessage = "Some previous error";
        var item = new DownloadItem { Size = 1000, SizeLeft = 500 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.HasErrorCleared);
        Assert.True(obs.IsGoodObservation);
    }

    [Fact]
    public void ErrorCleared_NotSetWhenFetchingMetadata()
    {
        var snapshot = ObservedSnapshot();
        snapshot.LastErrorMessage = "Some previous error";
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, ErrorMessage = "downloading metadata" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.False(obs.HasErrorCleared);
    }

    #endregion

    #region Warning Detection

    [Fact]
    public void Warning_InStatus()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, Status = "warning" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.HasWarning);
        Assert.True(obs.IsBadObservation);
    }

    [Fact]
    public void Warning_InTrackedDownloadStatus()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, TrackedDownloadStatus = "warn" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.HasWarning);
    }

    #endregion

    #region Fetching Metadata

    [Fact]
    public void FetchingMetadata_DetectedInErrorMessage()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, ErrorMessage = "downloading metadata" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsFetchingMetadata);
        Assert.True(obs.IsBadObservation);
        Assert.Null(obs.ErrorKey);
    }

    [Fact]
    public void FetchingMetadata_WithFetchingVariant()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, ErrorMessage = "fetching metadata from tracker" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsFetchingMetadata);
    }

    [Fact]
    public void FetchingMetadata_SuppressedByProgress()
    {
        var snapshot = ObservedSnapshot(30m, 700);
        var item = new DownloadItem { Size = 1000, SizeLeft = 400, ErrorMessage = "downloading metadata" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.False(obs.IsFetchingMetadata);
    }

    #endregion

    #region Recovery Detection

    [Fact]
    public void MeaningfulRecovery_WhenProgressAfterBadStreaks()
    {
        var snapshot = ObservedSnapshot(30m, 700);
        snapshot.ConsecutiveBadScans = 3;
        var item = new DownloadItem { Size = 1000, SizeLeft = 400 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsMeaningfulRecovery);
    }

    [Fact]
    public void MeaningfulRecovery_WhenProgressAfterDegradingState()
    {
        var snapshot = ObservedSnapshot(30m, 700);
        snapshot.State = HealthState.Degrading;
        var item = new DownloadItem { Size = 1000, SizeLeft = 400 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsMeaningfulRecovery);
    }

    [Fact]
    public void MeaningfulRecovery_WhenProgressAfterWatchingState()
    {
        var snapshot = ObservedSnapshot(30m, 700);
        snapshot.State = HealthState.Watching;
        var item = new DownloadItem { Size = 1000, SizeLeft = 400 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsMeaningfulRecovery);
    }

    [Fact]
    public void MeaningfulRecovery_WhenProgressAfterCriticalState()
    {
        var snapshot = ObservedSnapshot(30m, 700);
        snapshot.State = HealthState.Critical;
        var item = new DownloadItem { Size = 1000, SizeLeft = 400 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.IsMeaningfulRecovery);
    }

    [Fact]
    public void NotMeaningfulRecovery_WhenHealthy()
    {
        var snapshot = ObservedSnapshot(30m, 700);
        snapshot.State = HealthState.Healthy;
        snapshot.ConsecutiveBadScans = 0;
        var item = new DownloadItem { Size = 1000, SizeLeft = 400 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.False(obs.IsMeaningfulRecovery);
    }

    [Fact]
    public void SustainedProgress_WhenThreeConsecutiveGoodScans()
    {
        var snapshot = ObservedSnapshot(30m, 700);
        snapshot.ConsecutiveGoodScans = 3;
        var item = new DownloadItem { Size = 1000, SizeLeft = 400 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.HasSustainedProgress);
    }

    [Fact]
    public void NotSustainedProgress_WhenBelowThreshold()
    {
        var snapshot = ObservedSnapshot(30m, 700);
        snapshot.ConsecutiveGoodScans = 2;
        var item = new DownloadItem { Size = 1000, SizeLeft = 400 };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.False(obs.HasSustainedProgress);
    }

    #endregion

    #region Zero/Edge Cases

    [Fact]
    public void ZeroSize_ProgressIsNull()
    {
        var item = new DownloadItem { Size = 0, SizeLeft = 0 };
        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), FreshSnapshot(), DateTime.UtcNow);

        Assert.Null(obs.Progress);
    }

    [Fact]
    public void NegativeSize_ProgressIsNull()
    {
        var item = new DownloadItem { Size = -1, SizeLeft = 0 };
        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), FreshSnapshot(), DateTime.UtcNow);

        Assert.Null(obs.Progress);
    }

    [Fact]
    public void SizeLeftLargerThanSize_ProgressClampsToZero()
    {
        var item = new DownloadItem { Size = 100, SizeLeft = 200 };
        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), FreshSnapshot(), DateTime.UtcNow);

        Assert.Equal(0m, obs.Progress);
    }

    [Fact]
    public void FullyDownloaded_Progress100()
    {
        var item = new DownloadItem { Size = 1000, SizeLeft = 0 };
        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), FreshSnapshot(), DateTime.UtcNow);

        Assert.Equal(100m, obs.Progress);
    }

    [Fact]
    public void NullErrorMessage_NoError()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, ErrorMessage = null };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.False(obs.HasError);
        Assert.Null(obs.ErrorKey);
    }

    [Fact]
    public void EmptyErrorMessage_NoError()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, ErrorMessage = "" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.False(obs.HasError);
    }

    [Fact]
    public void WhitespaceErrorMessage_NoError()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, ErrorMessage = "   " };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.False(obs.HasError);
    }

    [Fact]
    public void Observation_CapturesProtocolAndClient()
    {
        var item = new DownloadItem
        {
            Size = 1000,
            SizeLeft = 500,
            Protocol = "torrent",
            DownloadClient = "qBittorrent",
            Indexer = "Prowlarr"
        };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), FreshSnapshot(), DateTime.UtcNow);

        Assert.Equal("torrent", obs.Protocol);
        Assert.Equal("qBittorrent", obs.DownloadClient);
        Assert.Equal("Prowlarr", obs.Indexer);
    }

    [Fact]
    public void Observation_UpdatesSnapshotSideEffects()
    {
        var snapshot = FreshSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, Status = "downloading" };

        var now = DateTime.UtcNow;
        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, now);
        HealthEvaluator.RecordObservation(snapshot, obs, now);

        Assert.Equal(50m, snapshot.LastProgress);
        Assert.Equal(500, snapshot.LastSizeLeft);
        Assert.Equal(1000, snapshot.LastSize);
        Assert.Equal("downloading", snapshot.LastStatus);
    }

    #endregion

    #region Usenet Behavior

    [Fact]
    public void Usenet_NoProgressNotBad()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, Protocol = "usenet" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.HasNoProgress);
        Assert.False(obs.IsBadObservation);
    }

    [Fact]
    public void Torrent_NoProgressIsBad()
    {
        var snapshot = ObservedSnapshot();
        var item = new DownloadItem { Size = 1000, SizeLeft = 500, Protocol = "torrent" };

        var obs = HealthEvaluator.BuildObservation(item, NewAttempt(), snapshot, DateTime.UtcNow);

        Assert.True(obs.HasNoProgress);
        Assert.True(obs.IsBadObservation);
    }

    #endregion
}
