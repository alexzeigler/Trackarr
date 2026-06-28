using Trackarr.Core.Health;
using Trackarr.Services.Media;

namespace Trackarr.Tests;

public class EvaluateScoringTests
{
    private static readonly HealthPolicy Policy = new();
    private readonly HealthEvaluator _evaluator = new(Policy);

    private static DownloadAttempt ActiveAttempt()
    {
        return new DownloadAttempt
        {
            Id = 1,
            DownloadId = "abc",
            Title = "Test",
            Status = AttemptStatus.Active,
            FirstSeenAt = DateTime.UtcNow.AddHours(-1)
        };
    }

    private static HealthSnapshot WithPriorObservation(int consecutiveBad = 0, int consecutiveGood = 0)
    {
        return new HealthSnapshot
        {
            LastObservationAt = DateTime.UtcNow.AddMinutes(-5),
            ConsecutiveBadScans = consecutiveBad,
            ConsecutiveGoodScans = consecutiveGood
        };
    }

    #region No Meaningful Change

    [Fact]
    public void NoChange_DefaultsToQueueSeen()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { Protocol = "usenet" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(0m, eval.ScoreDelta);
        Assert.Equal(HealthEventType.QueueSeen, eval.EventType);
    }

    #endregion

    #region Penalty Paths

    [Fact]
    public void Error_AppliesErrorPenalty()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation
        {
            HasError = true, IsBadObservation = true, ErrorKey = "some-error", ErrorMessage = "Something went wrong"
        };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(Policy.ErrorPenalty, eval.ScoreDelta);
        Assert.Equal(HealthEventType.ErrorReported, eval.EventType);
        Assert.Equal(Severity.Severe, eval.Severity);
    }

    [Fact]
    public void Warning_AppliesWarningPenalty()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { HasWarning = true, IsBadObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(Policy.WarningPenalty, eval.ScoreDelta);
        Assert.Equal(HealthEventType.WarningReported, eval.EventType);
        Assert.Equal(Severity.Warning, eval.Severity);
    }

    [Fact]
    public void Stalled_AppliesStalledPenalty()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { IsStalled = true, IsBadObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(Policy.StalledPenalty, eval.ScoreDelta);
        Assert.Equal(HealthEventType.Stalled, eval.EventType);
        Assert.Equal(Severity.Severe, eval.Severity);
    }

    [Fact]
    public void FetchingMetadata_AppliesFetchingPenalty()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { IsFetchingMetadata = true, IsBadObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(Policy.FetchingMetadataPenalty, eval.ScoreDelta);
        Assert.Equal(HealthEventType.FetchingMetadata, eval.EventType);
        Assert.Equal(Severity.Info, eval.Severity);
    }

    [Fact]
    public void NoProgress_AppliesNoProgressPenalty()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { HasNoProgress = true, IsBadObservation = true, Protocol = "torrent" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(Policy.NoProgressPenalty, eval.ScoreDelta);
        Assert.Equal(HealthEventType.NoProgress, eval.EventType);
        Assert.Equal(Severity.Negative, eval.Severity);
    }

    [Fact]
    public void NoProgress_Usenet_NoPenalty()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { HasNoProgress = true, IsBadObservation = false, Protocol = "usenet" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(0m, eval.ScoreDelta);
    }

    [Fact]
    public void RepeatedError_AppliesFlatPenalty()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation
        {
            HasError = true, IsRepeatedError = true, IsBadObservation = true, ErrorKey = "some-error"
        };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.True(eval.ScoreDelta < Policy.ErrorPenalty);
        Assert.Contains("Same error repeated", eval.Message);
    }

    [Fact]
    public void PreviousMediaFailure_AddsFlatPenalty()
    {
        var snapshot = WithPriorObservation();
        var summary = new MediaHealthSummary { FailureCount = 2 };
        var obs = new HealthObservation { HasNoProgress = true, IsBadObservation = true, Protocol = "torrent" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, summary, obs, 0, 0);

        Assert.True(eval.ScoreDelta < Policy.NoProgressPenalty);
        Assert.Contains("previous failed attempts", eval.Message);
    }

    [Fact]
    public void QualityDowngradedStillFailing_AppliesHeavyPenalty()
    {
        var snapshot = WithPriorObservation();
        var summary = new MediaHealthSummary { QualityDowngradeCount = 1 };
        var obs = new HealthObservation { HasError = true, IsBadObservation = true, ErrorKey = "err" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, summary, obs, 0, 0);

        Assert.Contains("Quality was downgraded", eval.Message);
        Assert.Equal(Severity.Critical, eval.Severity);
    }

    #endregion

    #region Reward Paths

    [Fact]
    public void ProgressIncrease_AppliesReward()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { HasProgressIncrease = true, IsGoodObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(Policy.ProgressIncreaseReward, eval.ScoreDelta);
        Assert.Equal(HealthEventType.ProgressIncreased, eval.EventType);
        Assert.Equal(Severity.Positive, eval.Severity);
    }

    [Fact]
    public void SizeLeftDecrease_AppliesReward()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { HasSizeLeftDecrease = true, IsGoodObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(Policy.SizeLeftDecreaseReward, eval.ScoreDelta);
        Assert.Equal(HealthEventType.SizeLeftDecreased, eval.EventType);
    }

    [Fact]
    public void BothProgressAndSizeLeft_CombinesRewards()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation
        {
            HasProgressIncrease = true, HasSizeLeftDecrease = true, IsGoodObservation = true
        };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(Policy.ProgressIncreaseReward + Policy.SizeLeftDecreaseReward, eval.ScoreDelta);
    }

    [Fact]
    public void ErrorCleared_AppliesFlatReward()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { HasErrorCleared = true, IsGoodObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(Policy.ErrorClearedReward, eval.ScoreDelta);
    }

    [Fact]
    public void MeaningfulRecovery_AppliesRewardAndCredit()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation
        {
            HasProgressIncrease = true, IsMeaningfulRecovery = true, IsGoodObservation = true
        };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.True(eval.ScoreDelta > Policy.ProgressIncreaseReward);
        Assert.True(eval.RecoveryCreditDelta > 0);
    }

    [Fact]
    public void SustainedProgress_AppliesBonusRewardAndCredit()
    {
        var snapshot = WithPriorObservation(consecutiveGood: 3);
        var obs = new HealthObservation
        {
            HasProgressIncrease = true, HasSustainedProgress = true, IsGoodObservation = true
        };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Contains("Sustained progress", eval.Message);
        Assert.True(eval.RecoveryCreditDelta >= 3);
    }

    #endregion

    #region Streak Multipliers

    [Fact]
    public void PenaltyStreak_Escalates()
    {
        var snapshot = WithPriorObservation(3);
        var obs = new HealthObservation { HasError = true, IsBadObservation = true, ErrorKey = "err" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.True(eval.ScoreDelta < Policy.ErrorPenalty);
    }

    [Fact]
    public void RewardStreak_Escalates()
    {
        var snapshot = WithPriorObservation(consecutiveGood: 3);
        var obs = new HealthObservation { HasProgressIncrease = true, IsGoodObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.True(eval.ScoreDelta > Policy.ProgressIncreaseReward);
    }

    [Fact]
    public void PenaltyStreak_CappedAtMax()
    {
        var snapshot = WithPriorObservation(100);
        var obs = new HealthObservation { HasError = true, IsBadObservation = true, ErrorKey = "err" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        var cappedPenalty = Policy.ErrorPenalty * Policy.PenaltyStreakCap;
        Assert.Equal(cappedPenalty, eval.ScoreDelta);
    }

    [Fact]
    public void RewardStreak_CappedAtMax()
    {
        var snapshot = WithPriorObservation(consecutiveGood: 100);
        var obs = new HealthObservation { HasProgressIncrease = true, IsGoodObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        var cappedReward = Policy.ProgressIncreaseReward * Policy.RewardStreakCap;
        Assert.Equal(cappedReward, eval.ScoreDelta);
    }

    [Fact]
    public void SingleStreak_NoMultiplier()
    {
        var snapshot = WithPriorObservation(0);
        var obs = new HealthObservation { HasError = true, IsBadObservation = true, ErrorKey = "err" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(Policy.ErrorPenalty, eval.ScoreDelta);
    }

    #endregion

    #region Recovery Credit Absorption

    [Fact]
    public void RecoveryCredit_AbsorbsPenalty()
    {
        var snapshot = WithPriorObservation();
        snapshot.RecoveryCredit = 10;
        var obs = new HealthObservation { HasNoProgress = true, IsBadObservation = true, Protocol = "torrent" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.True(eval.ScoreDelta > Policy.NoProgressPenalty);
        Assert.True(eval.RecoveryCreditDelta < 0);
    }

    [Fact]
    public void RecoveryCredit_CantMakePenaltyPositive()
    {
        var snapshot = WithPriorObservation();
        snapshot.RecoveryCredit = 25;
        var obs = new HealthObservation { HasNoProgress = true, IsBadObservation = true, Protocol = "torrent" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.True(eval.ScoreDelta >= 0);
    }

    [Fact]
    public void NoRecoveryCredit_FullPenalty()
    {
        var snapshot = WithPriorObservation();
        snapshot.RecoveryCredit = 0;
        var obs = new HealthObservation { HasError = true, IsBadObservation = true, ErrorKey = "err" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(Policy.ErrorPenalty, eval.ScoreDelta);
        Assert.Equal(0, eval.RecoveryCreditDelta);
    }

    #endregion

    #region Window Counts

    [Fact]
    public void BadObservation_IncrementsBadInWindow()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { HasError = true, IsBadObservation = true, ErrorKey = "err" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 3, 2);

        Assert.Equal(4, eval.BadScansInWindow);
        Assert.Equal(2, eval.GoodScansInWindow);
    }

    [Fact]
    public void GoodObservation_IncrementsGoodInWindow()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { HasProgressIncrease = true, IsGoodObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 3, 2);

        Assert.Equal(3, eval.BadScansInWindow);
        Assert.Equal(3, eval.GoodScansInWindow);
    }

    #endregion

    #region Event Priority

    [Fact]
    public void StalledNoConnections_TakesPriorityOverStalled()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { IsStalledNoConnections = true, IsStalled = true, IsBadObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(HealthEventType.StalledNoConnections, eval.EventType);
    }

    [Fact]
    public void ProgressAndRecovery_RecoveryTakesPriority()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation
        {
            HasProgressIncrease = true, IsMeaningfulRecovery = true, IsGoodObservation = true
        };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal(HealthEventType.RecoveryStarted, eval.EventType);
    }

    #endregion

    #region Reason Key

    [Fact]
    public void StalledNoConnections_ReasonKey()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { IsStalledNoConnections = true, IsStalled = true, IsBadObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal("stalled-no-connections", eval.ReasonKey);
    }

    [Fact]
    public void Stalled_ReasonKey()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { IsStalled = true, IsBadObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal("stalled", eval.ReasonKey);
    }

    [Fact]
    public void Progress_ReasonKey()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { HasProgressIncrease = true, IsGoodObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal("progress-detected", eval.ReasonKey);
    }

    [Fact]
    public void FetchingMetadata_ReasonKey()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { IsFetchingMetadata = true, IsBadObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal("stalled-fetching-metadata", eval.ReasonKey);
    }

    [Fact]
    public void NoProgress_ReasonKey()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { HasNoProgress = true, IsBadObservation = true, Protocol = "torrent" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal("no-progress", eval.ReasonKey);
    }

    [Fact]
    public void RepeatedError_ReasonKey()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation
        {
            HasError = true, IsRepeatedError = true, IsBadObservation = true, ErrorKey = "import-failed"
        };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal("repeated-error", eval.ReasonKey);
    }

    [Fact]
    public void Warning_ReasonKey()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation { HasWarning = true, IsBadObservation = true };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal("download-warning", eval.ReasonKey);
    }

    [Fact]
    public void MeaningfulRecovery_ReasonKey()
    {
        var snapshot = WithPriorObservation();
        var obs = new HealthObservation
        {
            HasProgressIncrease = true, IsMeaningfulRecovery = true, IsGoodObservation = true
        };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Equal("meaningful-recovery", eval.ReasonKey);
    }

    #endregion

    #region NoProgress Message Varies With Streak

    [Fact]
    public void NoProgress_ShortStreak_BasicMessage()
    {
        var snapshot = WithPriorObservation(1);
        var obs = new HealthObservation { HasNoProgress = true, IsBadObservation = true, Protocol = "torrent" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Contains("No progress detected", eval.Message);
    }

    [Fact]
    public void NoProgress_MediumStreak_RepeatedMessage()
    {
        var snapshot = WithPriorObservation(3);
        var obs = new HealthObservation { HasNoProgress = true, IsBadObservation = true, Protocol = "torrent" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Contains("Repeated lack of progress", eval.Message);
    }

    [Fact]
    public void NoProgress_LongStreak_ExtendedMessage()
    {
        var snapshot = WithPriorObservation(6);
        var obs = new HealthObservation { HasNoProgress = true, IsBadObservation = true, Protocol = "torrent" };

        var eval = _evaluator.Evaluate(ActiveAttempt(), snapshot, null, obs, 0, 0);

        Assert.Contains("extended period", eval.Message);
    }

    #endregion
}

public class CreateHealthEventTests
{
    [Fact]
    public void CreatesEvent_WithCorrectDeltas()
    {
        var attempt = new DownloadAttempt { Id = 1, Title = "Test" };
        var snapshot = new HealthSnapshot { RawScore = 65, Confidence = 30, RecoveryCredit = 5 };
        var before = new HealthStateBefore(70, 20, 0);
        var evaluation = new HealthEvaluation
        {
            EventType = HealthEventType.ProgressIncreased,
            Severity = Severity.Positive,
            ReasonKey = "progress-detected",
            Message = "Progress increased.",
            IsGoodObservation = true
        };
        var observation = new HealthObservation { Status = "downloading" };
        var now = DateTime.UtcNow;

        var evt = HealthEvaluator.CreateHealthEvent(attempt, snapshot, before, evaluation, observation, now);

        Assert.Equal(70, evt.ScoreBefore);
        Assert.Equal(65 - 70, evt.ScoreDelta);
        Assert.Equal(65, evt.ScoreAfter);
        Assert.Equal(20, evt.ConfidenceBefore);
        Assert.Equal(10, evt.ConfidenceDelta);
        Assert.Equal(30, evt.ConfidenceAfter);
        Assert.Equal(0, evt.RecoveryCreditBefore);
        Assert.Equal(5, evt.RecoveryCreditDelta);
        Assert.Equal(5, evt.RecoveryCreditAfter);
        Assert.True(evt.IsGoodObservation);
        Assert.Equal(now, evt.OccurredAt);
        Assert.Equal("downloading", evt.RawStatus);
    }
}
