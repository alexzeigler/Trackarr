namespace Trackarr.Core.Health;

public class DownloadHealthEvent
{
    #region Navigation

    public DownloadAttempt? DownloadAttempt { get; set; }

    #endregion

    #region Identity

    public int Id { get; set; }

    public int DownloadAttemptId { get; set; }

    #endregion

    #region Properties

    public DateTime OccurredAt { get; set; }

    public HealthEventType EventType { get; set; }
    public Severity Severity { get; set; }

    public string ReasonKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public decimal ScoreBefore { get; set; }
    public decimal ScoreDelta { get; set; }
    public decimal ScoreAfter { get; set; }

    public int ConfidenceBefore { get; set; }
    public int ConfidenceDelta { get; set; }
    public int ConfidenceAfter { get; set; }

    public int RecoveryCreditBefore { get; set; }
    public int RecoveryCreditDelta { get; set; }
    public int RecoveryCreditAfter { get; set; }

    public bool IsGoodObservation { get; set; }
    public bool IsBadObservation { get; set; }
    public bool IsMeaningfulRecovery { get; set; }
    public bool CountsTowardRemoval { get; set; }

    public string? Source { get; set; }

    public string? RawStatus { get; set; }
    public string? RawTrackedDownloadStatus { get; set; }
    public string? RawTrackedDownloadState { get; set; }
    public string? RawErrorMessage { get; set; }

    public string? MetadataJson { get; set; }

    #endregion
}
