using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Services.Media;
using Xunit;

namespace Trackarr.Tests;

/// <summary>
/// Characterization tests pinning the pure scoring behavior of <see cref="HealthEvaluator" />.
/// Two of these are explicit regression guards for bugs fixed during the refactor.
/// </summary>
public class HealthEvaluatorTests
{
    private static readonly HealthPolicy Policy = new();
    private readonly HealthEvaluator _evaluator = new(Policy);

    private static DownloadAttempt NewAttempt() => new()
    {
        Id = 1,
        DownloadId = "abc",
        Title = "Test Download",
        Status = AttemptStatus.Active,
        FirstSeenAt = DateTime.UtcNow.AddHours(-1)
    };

    [Fact]
    public void FirstObservation_ReturnsFirstSeenBaseline()
    {
        var attempt = NewAttempt();
        var snapshot = new HealthSnapshot(); // LastObservationAt == null => first observation
        var item = new DownloadItem { Size = 1000, SizeLeft = 500 };
        var now = DateTime.UtcNow;

        var observation = HealthEvaluator.BuildObservation(item, attempt, snapshot, now);
        var evaluation = _evaluator.Evaluate(attempt, snapshot, null, observation, 0, 0);

        Assert.Equal(HealthEventType.FirstSeen, evaluation.EventType);
        Assert.Equal("first-seen", evaluation.ReasonKey);
        Assert.Equal(0m, evaluation.ScoreDelta);
    }

    [Fact]
    public void ProgressIncrease_YieldsPositiveScoreDelta()
    {
        var attempt = NewAttempt();
        var now = DateTime.UtcNow;
        var snapshot = new HealthSnapshot
        {
            LastObservationAt = now.AddMinutes(-1),
            LastProgress = 10m,
            LastSizeLeft = 1000
        };
        var item = new DownloadItem { Size = 1000, SizeLeft = 500 }; // now 50%, size left fell

        var observation = HealthEvaluator.BuildObservation(item, attempt, snapshot, now);
        var evaluation = _evaluator.Evaluate(attempt, snapshot, null, observation, 0, 0);

        Assert.True(evaluation.IsGoodObservation);
        Assert.Equal(HealthEventType.ProgressIncreased, evaluation.EventType);
        Assert.True(evaluation.ScoreDelta > 0);
    }

    [Fact]
    public void StalledNoConnections_IsPenalizedExactlyOnce()
    {
        // Regression guard: the StalledNoConnections penalty used to be applied twice.
        var attempt = NewAttempt();
        var now = DateTime.UtcNow;
        var snapshot = new HealthSnapshot
        {
            LastObservationAt = now.AddMinutes(-1),
            ConsecutiveBadScans = 0 // streak of 1 => penalty multiplier of 1
        };
        var item = new DownloadItem
        {
            Size = 1000,
            SizeLeft = 1000,
            ErrorMessage = "The download is stalled with no connections"
        };

        var observation = HealthEvaluator.BuildObservation(item, attempt, snapshot, now);
        Assert.True(observation.IsStalledNoConnections);

        var evaluation = _evaluator.Evaluate(attempt, snapshot, null, observation, 0, 0);

        // Exactly one application of the -20 penalty (the bug produced -40).
        Assert.Equal((decimal)Policy.StalledNoConnectionsPenalty, evaluation.ScoreDelta);
    }

    [Fact]
    public void GoodObservation_AppliesBadScanDecay()
    {
        // Regression guard: the good-observation decay used to be overwritten and never applied.
        var now = DateTime.UtcNow;
        var snapshot = new HealthSnapshot { BadScansInLastTen = 0 };
        var evaluation = new HealthEvaluation
        {
            IsGoodObservation = true,
            BadScansInWindow = 5,
            GoodScansInWindow = 1
        };

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        Assert.Equal(5 - Policy.GoodObservationBadScanDecay, snapshot.BadScansInLastTen);
    }
}
