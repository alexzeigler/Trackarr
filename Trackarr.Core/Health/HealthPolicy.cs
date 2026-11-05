namespace Trackarr.Core.Health;

public class HealthPolicy
{
    #region Properties

    public int StartingScore { get; set; } = 70;
    public int MaximumScore { get; set; } = 100;
    public int MinimumVisibleScore { get; set; } = 0;

    public int RemovalConfidenceThreshold { get; set; } = 70;
    public int StartupGracePeriodMinutes { get; set; } = 10;

    public int MinimumBadScansBeforeRemoval { get; set; } = 8;
    public int RollingWindowSize { get; set; } = 10;
    public int RequiredBadScansInWindow { get; set; } = 8;
    public int RecentRecoveryProtectionScans { get; set; } = 3;

    public int RecoveryCreditMax { get; set; } = 25;

    public decimal PenaltyStreakEscalation { get; set; } = 1.5m;
    public decimal PenaltyStreakCap { get; set; } = 8.5m;
    public decimal RewardStreakEscalation { get; set; } = 1.5m;
    public decimal RewardStreakCap { get; set; } = 8.5m;

    public int HealthyThreshold { get; set; } = 85;
    public int StableThreshold { get; set; } = 65;
    public int WatchingThreshold { get; set; } = 40;
    public int DegradingThreshold { get; set; } = 15;

    public int ProgressIncreaseReward { get; set; } = 3;
    public int SizeLeftDecreaseReward { get; set; } = 3;
    // Not scored: "positive speed" is the same signal as progress (already rewarded), and the ETA isn't tracked
    // between scans. Kept so stored settings still load; hidden from the Health Policy page.
    public int SpeedPositiveReward { get; set; } = 2;
    public int EtaImprovedReward { get; set; } = 1;
    public int ErrorClearedReward { get; set; } = 8;
    public int RecoveryReward { get; set; } = 10;
    public int SustainedProgressReward { get; set; } = 5;

    public int NoProgressPenalty { get; set; } = -2;
    public int WarningPenalty { get; set; } = -8;
    public int ErrorPenalty { get; set; } = -15;
    public int StalledPenalty { get; set; } = -18;
    public int StalledNoConnectionsPenalty { get; set; } = -20;
    public int RepeatedErrorPenalty { get; set; } = -10;
    public int PreviousMediaFailurePenalty { get; set; } = -15;
    public int QualityDowngradedStillFailingPenalty { get; set; } = -25;
    public int ByteProgressProtectionMinutes { get; set; } = 10;
    public decimal FetchingMetadataPenalty { get; set; } = -20m;
    public int GoodObservationBadScanDecay { get; set; } = 2;

    #endregion
}
