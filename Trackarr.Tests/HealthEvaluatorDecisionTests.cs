using Trackarr.Core.Health;
using Trackarr.Services.Media;

namespace Trackarr.Tests;

public class BuildDecisionTests
{
    private static readonly HealthPolicy Policy = new();
    private readonly HealthEvaluator _evaluator = new(Policy);

    private static DownloadAttempt EligibleAttempt(DateTime? firstSeenAt = null)
    {
        return new DownloadAttempt
        {
            Id = 1,
            Status = AttemptStatus.Active,
            IsProtected = false,
            FirstSeenAt = firstSeenAt ?? DateTime.UtcNow.AddHours(-2)
        };
    }

    private static HealthSnapshot EligibleSnapshot()
    {
        return new HealthSnapshot
        {
            RawScore = -10,
            Confidence = 80,
            BadScanCount = 15,
            BadScansInLastTen = 9,
            RecoveryCredit = 0,
            LastByteProgressAt = null,
            LastMeaningfulRecoveryAt = null
        };
    }

    private static HealthEvaluation DefaultEvaluation()
    {
        return new HealthEvaluation { ReasonKey = "test", Message = "test evaluation" };
    }

    [Fact]
    public void AllConditionsMet_ShouldRemoveIsTrue()
    {
        var decision = _evaluator.BuildDecision(EligibleAttempt(), EligibleSnapshot(), DefaultEvaluation(),
            DateTime.UtcNow);

        Assert.True(decision.ShouldRemove);
        Assert.True(decision.ShouldReplace);
        Assert.Empty(decision.Restrictions);
        Assert.Equal(HealthState.Remove, decision.State);
        Assert.Equal("eligible-for-removal", decision.ReasonKey);
    }

    [Fact]
    public void ProtectedDownload_Restricted()
    {
        var attempt = EligibleAttempt();
        attempt.IsProtected = true;

        var decision = _evaluator.BuildDecision(attempt, EligibleSnapshot(), DefaultEvaluation(), DateTime.UtcNow);

        Assert.False(decision.ShouldRemove);
        Assert.Contains(decision.Restrictions, r => r.Contains("protected"));
    }

    [Fact]
    public void NonActiveStatus_Restricted()
    {
        var attempt = EligibleAttempt();
        attempt.Status = AttemptStatus.Imported;

        var decision = _evaluator.BuildDecision(attempt, EligibleSnapshot(), DefaultEvaluation(), DateTime.UtcNow);

        Assert.False(decision.ShouldRemove);
        Assert.Contains(decision.Restrictions, r => r.Contains("no longer active"));
    }

    [Fact]
    public void InsideGracePeriod_Restricted()
    {
        var attempt = EligibleAttempt(DateTime.UtcNow.AddMinutes(-1));

        var decision = _evaluator.BuildDecision(attempt, EligibleSnapshot(), DefaultEvaluation(), DateTime.UtcNow);

        Assert.False(decision.ShouldRemove);
        Assert.Contains(decision.Restrictions, r => r.Contains("grace period"));
    }

    [Fact]
    public void ExactlyAtGracePeriodBoundary_NotRestricted()
    {
        var now = DateTime.UtcNow;
        var attempt = EligibleAttempt(now.AddMinutes(-Policy.StartupGracePeriodMinutes));

        var decision = _evaluator.BuildDecision(attempt, EligibleSnapshot(), DefaultEvaluation(), now);

        Assert.DoesNotContain(decision.Restrictions, r => r.Contains("grace period"));
    }

    [Fact]
    public void ScoreAboveZero_Restricted()
    {
        var snapshot = EligibleSnapshot();
        snapshot.RawScore = 5;

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), DateTime.UtcNow);

        Assert.False(decision.ShouldRemove);
        Assert.Contains(decision.Restrictions, r => r.Contains("not dropped below zero"));
    }

    [Fact]
    public void ScoreExactlyZero_Restricted()
    {
        var snapshot = EligibleSnapshot();
        snapshot.RawScore = 0;

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), DateTime.UtcNow);

        Assert.False(decision.ShouldRemove);
        Assert.Contains(decision.Restrictions, r => r.Contains("not dropped below zero"));
    }

    [Fact]
    public void LowConfidence_Restricted()
    {
        var snapshot = EligibleSnapshot();
        snapshot.Confidence = Policy.RemovalConfidenceThreshold - 1;

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), DateTime.UtcNow);

        Assert.False(decision.ShouldRemove);
        Assert.Contains(decision.Restrictions, r => r.Contains("Confidence"));
    }

    [Fact]
    public void ExactlyAtConfidenceThreshold_NotRestricted()
    {
        var snapshot = EligibleSnapshot();
        snapshot.Confidence = Policy.RemovalConfidenceThreshold;

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), DateTime.UtcNow);

        Assert.DoesNotContain(decision.Restrictions, r => r.Contains("Confidence"));
    }

    [Fact]
    public void InsufficientBadScans_Restricted()
    {
        var snapshot = EligibleSnapshot();
        snapshot.BadScanCount = Policy.MinimumBadScansBeforeRemoval - 1;

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), DateTime.UtcNow);

        Assert.False(decision.ShouldRemove);
        Assert.Contains(decision.Restrictions, r => r.Contains("Bad scan count"));
    }

    [Fact]
    public void ExactlyAtMinimumBadScans_NotRestricted()
    {
        var snapshot = EligibleSnapshot();
        snapshot.BadScanCount = Policy.MinimumBadScansBeforeRemoval;

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), DateTime.UtcNow);

        Assert.DoesNotContain(decision.Restrictions, r => r.Contains("Bad scan count"));
    }

    [Fact]
    public void InsufficientBadScansInWindow_Restricted()
    {
        var snapshot = EligibleSnapshot();
        snapshot.BadScansInLastTen = Policy.RequiredBadScansInWindow - 1;

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), DateTime.UtcNow);

        Assert.False(decision.ShouldRemove);
        Assert.Contains(decision.Restrictions, r => r.Contains("Bad scans in the last"));
    }

    [Fact]
    public void RecentByteProgress_Restricted()
    {
        var now = DateTime.UtcNow;
        var snapshot = EligibleSnapshot();
        snapshot.LastByteProgressAt = now.AddMinutes(-1);

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), now);

        Assert.False(decision.ShouldRemove);
        Assert.Contains(decision.Restrictions, r => r.Contains("moved bytes recently"));
    }

    [Fact]
    public void OldByteProgress_NotRestricted()
    {
        var now = DateTime.UtcNow;
        var snapshot = EligibleSnapshot();
        snapshot.LastByteProgressAt = now.AddMinutes(-(Policy.ByteProgressProtectionMinutes + 1));

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), now);

        Assert.DoesNotContain(decision.Restrictions, r => r.Contains("moved bytes recently"));
    }

    [Fact]
    public void RecentRecovery_Restricted()
    {
        var now = DateTime.UtcNow;
        var snapshot = EligibleSnapshot();
        snapshot.LastMeaningfulRecoveryAt = now.AddMinutes(-1);

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), now,
            scansSinceRecovery: 1);

        Assert.False(decision.ShouldRemove);
        Assert.Contains(decision.Restrictions, r => r.Contains("meaningful recovery"));
    }

    [Fact]
    public void OldRecovery_NotRestricted()
    {
        var now = DateTime.UtcNow;
        var snapshot = EligibleSnapshot();
        snapshot.LastMeaningfulRecoveryAt = now.AddMinutes(-30);

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), now,
            scansSinceRecovery: Policy.RecentRecoveryProtectionScans);

        Assert.DoesNotContain(decision.Restrictions, r => r.Contains("meaningful recovery"));
    }

    [Fact]
    public void RecoveryProtection_CountsScans_NotMinutes()
    {
        // Regression guard for H6: 3 "scans" used to mean 3 minutes, so at a 5 minute interval a recovery
        // was unprotected by the very next scan.
        var now = DateTime.UtcNow;
        var snapshot = EligibleSnapshot();
        snapshot.LastMeaningfulRecoveryAt = now.AddMinutes(-(Policy.RecentRecoveryProtectionScans + 2));

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), now,
            scansSinceRecovery: 1);

        Assert.Contains(decision.Restrictions, r => r.Contains("meaningful recovery"));
    }

    [Fact]
    public void ShouldProtect_WhenRecoveryCreditExists()
    {
        var snapshot = EligibleSnapshot();
        snapshot.RecoveryCredit = 5;

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), DateTime.UtcNow);

        Assert.True(decision.ShouldProtect);
    }

    [Fact]
    public void ShouldNotProtect_WhenNoRecoveryCredit()
    {
        var snapshot = EligibleSnapshot();
        snapshot.RecoveryCredit = 0;

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), DateTime.UtcNow);

        Assert.False(decision.ShouldProtect);
    }

    [Fact]
    public void MultipleRestrictions_AllPresent()
    {
        var now = DateTime.UtcNow;
        var attempt = EligibleAttempt(now.AddMinutes(-1));
        attempt.IsProtected = true;

        var snapshot = EligibleSnapshot();
        snapshot.RawScore = 50;
        snapshot.Confidence = 10;
        snapshot.BadScanCount = 0;

        var decision = _evaluator.BuildDecision(attempt, snapshot, DefaultEvaluation(), now);

        Assert.False(decision.ShouldRemove);
        Assert.True(decision.Restrictions.Count >= 4);
    }

    [Fact]
    public void WhenNotRemovable_StateComesFromSnapshot()
    {
        var snapshot = EligibleSnapshot();
        snapshot.RawScore = 50;
        snapshot.State = HealthState.Stable;

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), DateTime.UtcNow);

        Assert.Equal(HealthState.Stable, decision.State);
    }

    [Fact]
    public void Decision_CopiesScoreFields()
    {
        var snapshot = EligibleSnapshot();
        snapshot.RawScore = -5;
        snapshot.VisibleScore = 0;
        snapshot.Confidence = 85;
        snapshot.RecoveryCredit = 3;

        var decision = _evaluator.BuildDecision(EligibleAttempt(), snapshot, DefaultEvaluation(), DateTime.UtcNow);

        Assert.Equal(-5, decision.RawScore);
        Assert.Equal(0, decision.VisibleScore);
        Assert.Equal(85, decision.Confidence);
        Assert.Equal(3, decision.RecoveryCredit);
    }
}
