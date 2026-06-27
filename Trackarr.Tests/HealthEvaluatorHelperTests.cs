using Trackarr.Core.Health;
using Trackarr.Services.Media;

namespace Trackarr.Tests;

public class NormalizeTitleTests
{
    [Fact]
    public void Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, HealthEvaluator.NormalizeTitle(null));
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, HealthEvaluator.NormalizeTitle(""));
    }

    [Fact]
    public void Whitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, HealthEvaluator.NormalizeTitle("   "));
    }

    [Fact]
    public void SimpleTitleLowered()
    {
        Assert.Equal("breaking-bad", HealthEvaluator.NormalizeTitle("Breaking Bad"));
    }

    [Fact]
    public void LeadingTrailingWhitespaceTrimmed()
    {
        Assert.Equal("test", HealthEvaluator.NormalizeTitle("  test  "));
    }

    [Fact]
    public void SpecialCharactersStripped()
    {
        Assert.Equal("hello-world", HealthEvaluator.NormalizeTitle("Hello! World?"));
    }

    [Fact]
    public void ConsecutiveDashesCollapsed()
    {
        Assert.Equal("a-b", HealthEvaluator.NormalizeTitle("a---b"));
    }

    [Fact]
    public void UnderscoresPreserved()
    {
        Assert.Equal("snake_case", HealthEvaluator.NormalizeTitle("snake_case"));
    }

    [Fact]
    public void HyphensPreserved()
    {
        Assert.Equal("kebab-case", HealthEvaluator.NormalizeTitle("kebab-case"));
    }

    [Fact]
    public void MultipleSpacesBecomeDashes()
    {
        Assert.Equal("a-b", HealthEvaluator.NormalizeTitle("a   b"));
    }

    [Fact]
    public void OnlySpecialChars_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, HealthEvaluator.NormalizeTitle("!!!@@@###"));
    }

    [Fact]
    public void MixedCaseAndNumbers()
    {
        Assert.Equal("season-3-episode-10", HealthEvaluator.NormalizeTitle("Season 3 Episode 10"));
    }

    [Fact]
    public void UnicodeLettersPreserved()
    {
        Assert.Equal("café", HealthEvaluator.NormalizeTitle("Café"));
    }

    [Fact]
    public void TabsAndNewlinesTreatedAsWhitespace()
    {
        Assert.Equal("a-b", HealthEvaluator.NormalizeTitle("a\t\nb"));
    }
}

public class DeriveStateFromScoreTests
{
    private readonly HealthEvaluator _evaluator;
    private readonly HealthPolicy _policy = new();

    public DeriveStateFromScoreTests()
    {
        _evaluator = new HealthEvaluator(_policy);
    }

    [Fact]
    public void NegativeScore_ReturnsRemove()
    {
        Assert.Equal(HealthState.Remove, _evaluator.DeriveStateFromScore(-1));
    }

    [Fact]
    public void Zero_ReturnsCritical()
    {
        Assert.Equal(HealthState.Critical, _evaluator.DeriveStateFromScore(0));
    }

    [Fact]
    public void BelowDegradingThreshold_ReturnsCritical()
    {
        Assert.Equal(HealthState.Critical, _evaluator.DeriveStateFromScore(_policy.DegradingThreshold - 1));
    }

    [Fact]
    public void AtDegradingThreshold_ReturnsDegrading()
    {
        Assert.Equal(HealthState.Degrading, _evaluator.DeriveStateFromScore(_policy.DegradingThreshold));
    }

    [Fact]
    public void BelowWatchingThreshold_ReturnsDegrading()
    {
        Assert.Equal(HealthState.Degrading, _evaluator.DeriveStateFromScore(_policy.WatchingThreshold - 1));
    }

    [Fact]
    public void AtWatchingThreshold_ReturnsWatching()
    {
        Assert.Equal(HealthState.Watching, _evaluator.DeriveStateFromScore(_policy.WatchingThreshold));
    }

    [Fact]
    public void BelowStableThreshold_ReturnsWatching()
    {
        Assert.Equal(HealthState.Watching, _evaluator.DeriveStateFromScore(_policy.StableThreshold - 1));
    }

    [Fact]
    public void AtStableThreshold_ReturnsStable()
    {
        Assert.Equal(HealthState.Stable, _evaluator.DeriveStateFromScore(_policy.StableThreshold));
    }

    [Fact]
    public void BelowHealthyThreshold_ReturnsStable()
    {
        Assert.Equal(HealthState.Stable, _evaluator.DeriveStateFromScore(_policy.HealthyThreshold - 1));
    }

    [Fact]
    public void AtHealthyThreshold_ReturnsHealthy()
    {
        Assert.Equal(HealthState.Healthy, _evaluator.DeriveStateFromScore(_policy.HealthyThreshold));
    }

    [Fact]
    public void AtMaximumScore_ReturnsHealthy()
    {
        Assert.Equal(HealthState.Healthy, _evaluator.DeriveStateFromScore(_policy.MaximumScore));
    }

    [Fact]
    public void AboveMaximumScore_StillReturnsHealthy()
    {
        Assert.Equal(HealthState.Healthy, _evaluator.DeriveStateFromScore(_policy.MaximumScore + 50));
    }

    [Fact]
    public void VeryNegativeScore_ReturnsRemove()
    {
        Assert.Equal(HealthState.Remove, _evaluator.DeriveStateFromScore(-1000));
    }
}

public class ApplyEvaluationTests
{
    private readonly HealthEvaluator _evaluator;
    private readonly HealthPolicy _policy = new();

    public ApplyEvaluationTests()
    {
        _evaluator = new HealthEvaluator(_policy);
    }

    [Fact]
    public void ScoreClampedToMaximum()
    {
        var snapshot = new HealthSnapshot { RawScore = 95 };
        var evaluation = new HealthEvaluation { ScoreDelta = 50, IsGoodObservation = true };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(_policy.MaximumScore, snapshot.RawScore);
    }

    [Fact]
    public void ScoreClampedToNegativeMaximum()
    {
        var snapshot = new HealthSnapshot { RawScore = -90 };
        var evaluation = new HealthEvaluation { ScoreDelta = -50, IsBadObservation = true };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(-_policy.MaximumScore, snapshot.RawScore);
    }

    [Fact]
    public void VisibleScoreClampedToMinimum()
    {
        var snapshot = new HealthSnapshot { RawScore = 5 };
        var evaluation = new HealthEvaluation { ScoreDelta = -10, IsBadObservation = true };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.True(snapshot.VisibleScore >= _policy.MinimumVisibleScore);
    }

    [Fact]
    public void GoodObservation_IncrementsGoodCounters()
    {
        var snapshot = new HealthSnapshot { GoodScanCount = 5, ConsecutiveGoodScans = 2, ConsecutiveBadScans = 3 };
        var evaluation = new HealthEvaluation { IsGoodObservation = true };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(6, snapshot.GoodScanCount);
        Assert.Equal(3, snapshot.ConsecutiveGoodScans);
        Assert.Equal(0, snapshot.ConsecutiveBadScans);
        Assert.Equal(now, snapshot.LastGoodObservationAt);
    }

    [Fact]
    public void BadObservation_IncrementsBadCounters()
    {
        var snapshot = new HealthSnapshot { BadScanCount = 5, ConsecutiveBadScans = 2, ConsecutiveGoodScans = 3 };
        var evaluation = new HealthEvaluation { IsBadObservation = true };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(6, snapshot.BadScanCount);
        Assert.Equal(3, snapshot.ConsecutiveBadScans);
        Assert.Equal(0, snapshot.ConsecutiveGoodScans);
        Assert.Equal(now, snapshot.LastBadObservationAt);
    }

    [Fact]
    public void NeutralObservation_ResetsBothStreaks()
    {
        var snapshot = new HealthSnapshot { ConsecutiveGoodScans = 5, ConsecutiveBadScans = 3 };
        var evaluation = new HealthEvaluation { IsGoodObservation = false, IsBadObservation = false };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(0, snapshot.ConsecutiveGoodScans);
        Assert.Equal(0, snapshot.ConsecutiveBadScans);
    }

    [Fact]
    public void RecoveryCreditClampedToMax()
    {
        var snapshot = new HealthSnapshot { RecoveryCredit = 20 };
        var evaluation = new HealthEvaluation { RecoveryCreditDelta = 50 };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(_policy.RecoveryCreditMax, snapshot.RecoveryCredit);
    }

    [Fact]
    public void RecoveryCreditClampedToZero()
    {
        var snapshot = new HealthSnapshot { RecoveryCredit = 5 };
        var evaluation = new HealthEvaluation { RecoveryCreditDelta = -20 };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(0, snapshot.RecoveryCredit);
    }

    [Fact]
    public void ScoreChange_UpdatesLastScoreChangedAt()
    {
        var snapshot = new HealthSnapshot { RawScore = 70 };
        var evaluation = new HealthEvaluation { ScoreDelta = 5, IsGoodObservation = true };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(now, snapshot.LastScoreChangedAt);
        Assert.Equal(5, snapshot.LastScoreChange);
    }

    [Fact]
    public void NoScoreChange_DoesNotUpdateLastScoreChangedAt()
    {
        var snapshot = new HealthSnapshot { RawScore = 70, LastScoreChangedAt = null };
        var evaluation = new HealthEvaluation { ScoreDelta = 0 };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Null(snapshot.LastScoreChangedAt);
    }

    [Fact]
    public void MeaningfulRecovery_UpdatesTimestamp()
    {
        var snapshot = new HealthSnapshot();
        var evaluation = new HealthEvaluation { IsMeaningfulRecovery = true, IsGoodObservation = true };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(now, snapshot.LastMeaningfulRecoveryAt);
    }

    [Fact]
    public void LastObservationAt_AlwaysUpdated()
    {
        var snapshot = new HealthSnapshot();
        var evaluation = new HealthEvaluation();
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(now, snapshot.LastObservationAt);
    }

    [Fact]
    public void BadScansInLastTen_DecayedOnGoodObservation()
    {
        var snapshot = new HealthSnapshot { BadScansInLastTen = 0 };
        var evaluation = new HealthEvaluation { IsGoodObservation = true, BadScansInWindow = 7 };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(7 - _policy.GoodObservationBadScanDecay, snapshot.BadScansInLastTen);
    }

    [Fact]
    public void BadScansInLastTen_NeverGoesNegative()
    {
        var snapshot = new HealthSnapshot { BadScansInLastTen = 0 };
        var evaluation = new HealthEvaluation { IsGoodObservation = true, BadScansInWindow = 1 };
        var now = DateTime.UtcNow;

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.True(snapshot.BadScansInLastTen >= 0);
    }
}
