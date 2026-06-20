namespace Trackarr.Core.Health;

public class HealthSnapshot
{
    public int Id { get; set; }

    public int DownloadAttemptId { get; set; }
    public DownloadAttempt? DownloadAttempt { get; set; }
    public DateTime? LastByteProgressAt { get; set; }

    public decimal RawScore { get; set; } = 70;
    public int VisibleScore { get; set; } = 70;

    public int Confidence { get; set; }
    public int RecoveryCredit { get; set; }

    public HealthState State { get; set; } = HealthState.New;

    public int GoodScanCount { get; set; }
    public int BadScanCount { get; set; }

    public int ConsecutiveGoodScans { get; set; }
    public int ConsecutiveBadScans { get; set; }

    public int BadScansInLastTen { get; set; }
    public int GoodScansInLastTen { get; set; }

    public decimal? LastProgress { get; set; }
    public long? LastSizeLeft { get; set; }
    public long? LastSize { get; set; }

    public string? LastStatus { get; set; }
    public string? LastTrackedDownloadStatus { get; set; }
    public string? LastTrackedDownloadState { get; set; }

    public string? LastErrorKey { get; set; }
    public string? LastErrorMessage { get; set; }

    public DateTime? LastObservationAt { get; set; }
    public DateTime? LastGoodObservationAt { get; set; }
    public DateTime? LastBadObservationAt { get; set; }
    public DateTime? LastMeaningfulRecoveryAt { get; set; }

    public decimal LastScoreChange { get; set; }
    public DateTime? LastScoreChangedAt { get; set; }

    public string? LastReasonKey { get; set; }
    public string? LastReasonMessage { get; set; }

    public bool IsEligibleForRemoval { get; set; }
    public bool IsRemovalRestricted { get; set; }
    public string? RemovalRestrictionReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}