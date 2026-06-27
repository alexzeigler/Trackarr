namespace Trackarr.Core.Health;

public class MediaHealthSummary
{
    #region Identity

    public int Id { get; set; }

    public TrackedMediaType MediaType { get; set; } = TrackedMediaType.Unknown;

    public int? MovieId { get; set; }
    public int? SeriesId { get; set; }
    public int? EpisodeId { get; set; }

    #endregion

    #region Properties

    public int AttemptCount { get; set; }
    public int ActiveAttemptCount { get; set; }
    public int FailureCount { get; set; }
    public int SuccessCount { get; set; }
    public int RemovalCount { get; set; }
    public int ReplacementCount { get; set; }

    public DateTime? LastAttemptAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public DateTime? LastRemovalAt { get; set; }
    public DateTime? LastReplacementAt { get; set; }

    public HealthState CurrentState { get; set; } = HealthState.Unknown;

    public int CurrentBestScore { get; set; }
    public int CurrentWorstScore { get; set; }

    public int SearchAttemptCount { get; set; }
    public int QualityDowngradeCount { get; set; }

    public string? LastQualityProfile { get; set; }
    public string? LowestQualityTried { get; set; }

    public bool IsStuck { get; set; }
    public string? StuckReasonKey { get; set; }
    public string? StuckReasonMessage { get; set; }
    public DateTime? StuckSince { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    #endregion
}
