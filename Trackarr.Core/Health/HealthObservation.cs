namespace Trackarr.Core.Health;

public class HealthObservation
{
    #region Properties

    public int DownloadAttemptId { get; set; }

    public DateTime ObservedAt { get; set; }

    public string DownloadId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    public decimal? Progress { get; set; }
    public long? Size { get; set; }
    public long? SizeLeft { get; set; }

    public string? Status { get; set; }
    public string? TrackedDownloadStatus { get; set; }
    public string? TrackedDownloadState { get; set; }

    public string? ErrorMessage { get; set; }
    public string? ErrorKey { get; set; }

    public bool HasProgressIncrease { get; set; }
    public bool HasSizeLeftDecrease { get; set; }
    public bool HasPositiveSpeed { get; set; }
    public bool HasError { get; set; }
    public bool HasWarning { get; set; }
    public bool IsStalled { get; set; }
    public bool IsStalledNoConnections { get; set; }
    public bool IsMeaningfulRecovery { get; set; }

    public bool HasNoProgress { get; set; }
    public bool HasErrorCleared { get; set; }
    public bool IsRepeatedError { get; set; }
    public bool IsGoodObservation { get; set; }
    public bool IsBadObservation { get; set; }
    public bool HasSustainedProgress { get; set; }

    public string? DownloadClient { get; set; }
    public string? Indexer { get; set; }
    public string? Protocol { get; set; }
    public bool IsFetchingMetadata { get; set; }

    #endregion
}
