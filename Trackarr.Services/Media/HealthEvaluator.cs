using Trackarr.Core.Health;
using Trackarr.Core.Queue;

namespace Trackarr.Services.Media;

/// <summary>
///     The pure scoring engine for download health. Holds no database or I/O dependencies, so it can be
///     unit-tested in isolation: given an item and the current snapshot/attempt state, it produces an
///     observation, an evaluation (score deltas + reasons), a mutated snapshot and a removal decision.
///     <see cref="HealthService" /> owns the persistence and feeds this class the rolling-window counts.
/// </summary>
public sealed class HealthEvaluator(HealthPolicy? policy = null)
{
    #region Observation

    private static readonly string[] ImportWaitStates = ["importPending", "importBlocked", "importing"];

    /// <summary>
    ///     Returns why an item should not be scored right now, or null if it should be. These are states where
    ///     no byte progress is expected, so "no progress" or a warning says nothing about the release itself.
    ///     Values follow the Radarr/Sonarr v3 queue API (<c>status</c> and <c>trackedDownloadState</c>).
    /// </summary>
    public static string? GetScoringExemption(DownloadItem item)
    {
        if (IsValue(item.Status, "downloadClientUnavailable")) return "Download client is unavailable.";
        if (IsValue(item.Status, "queued")) return "Queued in the download client.";
        if (IsValue(item.Status, "paused")) return "Paused in the download client.";
        if (IsValue(item.Status, "delay")) return "Held by a delay profile.";

        if (item.SizeLeft == 0 && ImportWaitStates.Any(s => IsValue(item.TrackedDownloadState, s)))
            return "Download finished and is waiting on import.";

        return null;
    }

    /// <summary>True when any queue item reports its download client as unavailable.</summary>
    public static bool IsDownloadClientUnavailable(IEnumerable<DownloadItem> queue)
    {
        return queue.Any(i => IsValue(i.Status, "downloadClientUnavailable"));
    }

    private static bool IsValue(string? value, string expected)
    {
        return string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    public static HealthObservation BuildObservation(DownloadItem item, DownloadAttempt attempt,
        HealthSnapshot snapshot, DateTime now)
    {
        var progress = CalculateProgress(item.Size, item.SizeLeft);

        var progressIncreased = progress.HasValue && snapshot.LastProgress.HasValue &&
                                progress.Value > snapshot.LastProgress.Value;
        var sizeLeftDecreased = snapshot.LastSizeLeft.HasValue && item.SizeLeft < snapshot.LastSizeLeft.Value;
        var isActivelyProgressing = progressIncreased || sizeLeftDecreased;

        var isFetchingMetadata = !isActivelyProgressing &&
                                 ContainsAny(item.ErrorMessage, "downloading metadata", "fetching metadata");
        var errorKey = isFetchingMetadata ? null : NormalizeError(item.ErrorMessage);
        var hasError = !isActivelyProgressing && !isFetchingMetadata && !string.IsNullOrWhiteSpace(item.ErrorMessage);
        var hasWarning = !isActivelyProgressing && (ContainsAny(item.Status, "warn", "warning") ||
                                                    ContainsAny(item.TrackedDownloadStatus, "warn", "warning"));

        var isStalled = !isActivelyProgressing && (ContainsAny(item.Status, "stalled", "stuck") ||
                                                   ContainsAny(item.TrackedDownloadStatus, "stalled", "stuck") ||
                                                   ContainsAny(item.TrackedDownloadState, "stalled", "stuck") ||
                                                   ContainsAny(item.ErrorMessage, "stalled", "stuck"));
        var isStalledNoConnections = !isActivelyProgressing && isStalled &&
                                     ContainsAny(item.ErrorMessage, "no connections", "no peers", "0 peers");

        var errorCleared = !hasError && !isFetchingMetadata && !string.IsNullOrWhiteSpace(snapshot.LastErrorMessage);
        var repeatedError = !string.IsNullOrWhiteSpace(errorKey) && errorKey == snapshot.LastErrorKey;
        var hasPreviousObservation = snapshot.LastObservationAt.HasValue;
        var hasNoProgress = hasPreviousObservation && !isActivelyProgressing && !isFetchingMetadata && !hasError &&
                            !hasWarning && !isStalled;
        var isGood = isActivelyProgressing || errorCleared;
        var isUsenet = string.Equals(item.Protocol, "usenet", StringComparison.OrdinalIgnoreCase);
        var isBad = !isActivelyProgressing && ((hasNoProgress && !isUsenet) || hasError || hasWarning || isStalled ||
                                               isStalledNoConnections || isFetchingMetadata || repeatedError);
        var meaningfulRecovery = isGood && (snapshot.ConsecutiveBadScans > 0 ||
                                            snapshot.State is HealthState.Watching or HealthState.Degrading
                                                or HealthState.Critical);
        var sustainedProgress = isGood && snapshot.ConsecutiveGoodScans >= 3;

        snapshot.LastProgress = progress;
        snapshot.LastSizeLeft = item.SizeLeft;
        snapshot.LastSize = item.Size;
        snapshot.LastStatus = item.Status;
        snapshot.LastTrackedDownloadStatus = item.TrackedDownloadStatus;
        snapshot.LastTrackedDownloadState = item.TrackedDownloadState;
        snapshot.LastErrorKey = errorKey;
        snapshot.LastErrorMessage = isFetchingMetadata ? null : item.ErrorMessage;

        if (isActivelyProgressing) snapshot.LastByteProgressAt = now;

        return new HealthObservation
        {
            DownloadAttemptId = attempt.Id,
            ObservedAt = now,
            DownloadId = attempt.DownloadId,
            Title = item.Title ?? attempt.Title,
            Progress = progress,
            Size = item.Size,
            SizeLeft = item.SizeLeft,
            Status = item.Status,
            TrackedDownloadStatus = item.TrackedDownloadStatus,
            TrackedDownloadState = item.TrackedDownloadState,
            ErrorMessage = item.ErrorMessage,
            ErrorKey = errorKey,
            HasProgressIncrease = progressIncreased,
            HasSizeLeftDecrease = sizeLeftDecreased,
            HasPositiveSpeed = isActivelyProgressing,
            HasError = hasError,
            HasWarning = hasWarning,
            IsStalled = isStalled,
            IsStalledNoConnections = isStalledNoConnections,
            IsFetchingMetadata = isFetchingMetadata,
            IsMeaningfulRecovery = meaningfulRecovery,
            DownloadClient = item.DownloadClient,
            Indexer = item.Indexer,
            Protocol = item.Protocol,
            HasNoProgress = hasNoProgress,
            HasErrorCleared = errorCleared,
            IsRepeatedError = repeatedError,
            IsGoodObservation = isGood,
            IsBadObservation = isBad,
            HasSustainedProgress = sustainedProgress
        };
    }

    #endregion

    #region Evaluation

    /// <summary>
    ///     Scores a single observation. <paramref name="recentBadInWindow" /> and
    ///     <paramref name="recentGoodInWindow" /> are the bad/good counts of the prior rolling window
    ///     (excluding this observation); the caller loads them from history.
    /// </summary>
    public HealthEvaluation Evaluate(DownloadAttempt attempt, HealthSnapshot snapshot, MediaHealthSummary? mediaSummary,
        HealthObservation observation, int recentBadInWindow, int recentGoodInWindow)
    {
        if (snapshot.LastObservationAt is null)
        {
            return new HealthEvaluation
            {
                EventType = HealthEventType.FirstSeen,
                ReasonKey = "first-seen",
                Message = "First health observation; establishing baseline.",
                Severity = Severity.Info,
                BadScansInWindow = 0,
                GoodScansInWindow = 0
            };
        }

        var badStreak = snapshot.ConsecutiveBadScans + (observation.IsBadObservation ? 1 : 0);
        var goodStreak = snapshot.ConsecutiveGoodScans + (observation.IsGoodObservation ? 1 : 0);

        var penaltyMultiplier = StreakMultiplier(badStreak, _policy.PenaltyStreakEscalation, _policy.PenaltyStreakCap);
        var rewardMultiplier = StreakMultiplier(goodStreak, _policy.RewardStreakEscalation, _policy.RewardStreakCap);

        var recoveryCreditDelta = 0;

        List<string> reasons = [];
        List<HealthEventType> eventTypes = [];
        var severity = Severity.Info;

        decimal coreReward = 0;
        decimal flatReward = 0;

        if (observation.HasProgressIncrease)
        {
            coreReward += _policy.ProgressIncreaseReward;
            reasons.Add("Progress increased.");
            eventTypes.Add(HealthEventType.ProgressIncreased);
            severity = Severity.Positive;
        }

        if (observation.HasSizeLeftDecrease)
        {
            coreReward += _policy.SizeLeftDecreaseReward;
            reasons.Add("Size left decreased.");
            eventTypes.Add(HealthEventType.SizeLeftDecreased);
            severity = Severity.Positive;
        }

        if (observation.HasErrorCleared)
        {
            flatReward += _policy.ErrorClearedReward;
            reasons.Add("Error cleared.");
            eventTypes.Add(HealthEventType.ErrorCleared);
            severity = Severity.Positive;
        }

        if (observation.IsMeaningfulRecovery)
        {
            flatReward += _policy.RecoveryReward;
            recoveryCreditDelta += 10;
            reasons.Add("Download recovered after bad behavior.");
            eventTypes.Add(HealthEventType.RecoveryStarted);
            severity = Severity.Positive;
        }

        decimal corePenalty = 0;
        decimal flatPenalty = 0;

        if (observation.IsFetchingMetadata)
        {
            corePenalty += _policy.FetchingMetadataPenalty;
            reasons.Add("Fetching metadata.");
            eventTypes.Add(HealthEventType.FetchingMetadata);
            severity = Severity.Info;
        }
        else if (observation.IsStalledNoConnections)
        {
            corePenalty += _policy.StalledNoConnectionsPenalty;
            reasons.Add("Download is stalled with no connections.");
            eventTypes.Add(HealthEventType.StalledNoConnections);
            severity = Severity.Critical;
        }
        else if (observation.IsStalled)
        {
            corePenalty += _policy.StalledPenalty;
            reasons.Add("Download appears stalled.");
            eventTypes.Add(HealthEventType.Stalled);
            severity = Severity.Severe;
        }
        else if (observation.HasError)
        {
            corePenalty += _policy.ErrorPenalty;
            reasons.Add("Error reported.");
            eventTypes.Add(HealthEventType.ErrorReported);
            severity = Severity.Severe;
        }
        else if (observation.HasWarning)
        {
            corePenalty += _policy.WarningPenalty;
            reasons.Add("Warning reported.");
            eventTypes.Add(HealthEventType.WarningReported);
            severity = Severity.Warning;
        }
        else if (observation.HasNoProgress && !IsUsenet(observation))
        {
            corePenalty += _policy.NoProgressPenalty;
            reasons.Add(badStreak >= 6 ? "No progress for an extended period." :
                badStreak >= 3 ? "Repeated lack of progress." : "No progress detected.");
            eventTypes.Add(HealthEventType.NoProgress);
            severity = Severity.Negative;
        }

        if (observation.IsRepeatedError)
        {
            flatPenalty += _policy.RepeatedErrorPenalty;
            reasons.Add("Same error repeated.");
            eventTypes.Add(HealthEventType.ErrorRepeated);
        }

        if (observation.IsBadObservation && mediaSummary is { QualityDowngradeCount: > 0 })
        {
            flatPenalty += _policy.QualityDowngradedStillFailingPenalty;
            reasons.Add("Quality was downgraded and the download is still failing.");
            eventTypes.Add(HealthEventType.StatusWorsened);
            severity = Severity.Critical;
        }
        else if (observation.IsBadObservation && mediaSummary is { FailureCount: > 0 })
        {
            flatPenalty += _policy.PreviousMediaFailurePenalty;
            reasons.Add("This media has previous failed attempts.");
            eventTypes.Add(HealthEventType.StatusWorsened);
        }

        if (observation.HasSustainedProgress)
        {
            flatReward += _policy.SustainedProgressReward;
            recoveryCreditDelta += 3;
            reasons.Add("Sustained progress detected.");
            eventTypes.Add(HealthEventType.SustainedProgress);
            severity = Severity.Positive;
        }

        var reward = coreReward * rewardMultiplier + flatReward;
        var penalty = corePenalty * penaltyMultiplier + flatPenalty;

        if (penalty < 0 && snapshot.RecoveryCredit > 0)
        {
            var absorbed = Math.Min(snapshot.RecoveryCredit, (int)Math.Ceiling(Math.Abs(penalty)));
            penalty += absorbed;
            recoveryCreditDelta -= absorbed;
            reasons.Add($"Recovery credit absorbed {absorbed} penalty points.");
            eventTypes.Add(HealthEventType.RecoveryCreditApplied);
        }

        var scoreDelta = reward + penalty;

        var badInWindow = recentBadInWindow + (observation.IsBadObservation ? 1 : 0);
        var goodInWindow = recentGoodInWindow + (observation.IsGoodObservation ? 1 : 0);

        if (reasons.Count == 0)
        {
            reasons.Add("No meaningful health change detected.");
            eventTypes.Add(HealthEventType.QueueSeen);
        }

        return new HealthEvaluation
        {
            ScoreDelta = scoreDelta,
            RecoveryCreditDelta = recoveryCreditDelta,
            EventType = SelectEventType(eventTypes),
            ReasonKey = BuildReasonKey(observation, eventTypes),
            Message = string.Join(" ", reasons),
            Severity = severity,
            IsGoodObservation = observation.IsGoodObservation,
            IsBadObservation = observation.IsBadObservation,
            IsMeaningfulRecovery = observation.IsMeaningfulRecovery,
            CountsTowardRemoval = observation.IsBadObservation,
            BadScansInWindow = badInWindow,
            GoodScansInWindow = goodInWindow
        };
    }

    #endregion

    #region Application

    public void ApplyEvaluation(HealthSnapshot snapshot, HealthEvaluation evaluation, DateTime now)
    {
        var previousScore = snapshot.RawScore;
        var previousRecoveryCredit = snapshot.RecoveryCredit;

        snapshot.RawScore =
            Math.Clamp(previousScore + evaluation.ScoreDelta, -_policy.MaximumScore, _policy.MaximumScore);
        snapshot.VisibleScore = Math.Clamp((int)Math.Round(snapshot.RawScore), _policy.MinimumVisibleScore,
            _policy.MaximumScore);
        snapshot.RecoveryCredit = Math.Clamp(previousRecoveryCredit + evaluation.RecoveryCreditDelta, 0,
            _policy.RecoveryCreditMax);
        // The window is rebuilt from events every scan, so the decay has to be applied to the whole window: each
        // good scan still in it forgives GoodObservationBadScanDecay bad ones. (Subtracting only on the good scan
        // itself was undone by the next scan's recount.)
        snapshot.BadScansInLastTen = Math.Max(0,
            evaluation.BadScansInWindow - _policy.GoodObservationBadScanDecay * evaluation.GoodScansInWindow);
        snapshot.GoodScansInLastTen = evaluation.GoodScansInWindow;

        snapshot.LastScoreChange = snapshot.RawScore - previousScore;

        if (snapshot.LastScoreChange != 0) snapshot.LastScoreChangedAt = now;

        if (evaluation.IsGoodObservation)
        {
            snapshot.GoodScanCount++;
            snapshot.ConsecutiveGoodScans++;
            snapshot.ConsecutiveBadScans = 0;
            snapshot.LastGoodObservationAt = now;
        }

        if (evaluation.IsBadObservation)
        {
            snapshot.BadScanCount++;
            snapshot.ConsecutiveBadScans++;
            snapshot.ConsecutiveGoodScans = 0;
            snapshot.LastBadObservationAt = now;
        }

        if (!evaluation.IsGoodObservation && !evaluation.IsBadObservation)
        {
            snapshot.ConsecutiveGoodScans = 0;
            snapshot.ConsecutiveBadScans = 0;
        }

        if (evaluation.IsMeaningfulRecovery) snapshot.LastMeaningfulRecoveryAt = now;

        snapshot.Confidence = ComputeConfidence(snapshot);
        snapshot.State = GetState(snapshot.RawScore);
        snapshot.LastReasonKey = evaluation.ReasonKey;
        snapshot.LastReasonMessage = evaluation.Message;
        snapshot.LastObservationAt = now;
        snapshot.UpdatedAt = now;
    }

    #endregion

    #region Decision

    /// <param name="scansSinceRecovery">
    ///     Scans since <see cref="HealthSnapshot.LastMeaningfulRecoveryAt" />, counting this one; the caller counts
    ///     them from history. Null when unknown, which keeps a recovered download protected.
    /// </param>
    public HealthDecision BuildDecision(DownloadAttempt attempt, HealthSnapshot snapshot, HealthEvaluation evaluation,
        DateTime now, int? scansSinceRecovery = null)
    {
        List<string> restrictions = [];

        var age = now - attempt.FirstSeenAt;

        if (attempt.IsProtected) restrictions.Add("Download is protected.");

        if (attempt.Status != AttemptStatus.Active) restrictions.Add("Download attempt is no longer active.");

        if (age < TimeSpan.FromMinutes(_policy.StartupGracePeriodMinutes))
        {
            restrictions.Add(
                $"Download is inside the {_policy.StartupGracePeriodMinutes} minute startup grace period.");
        }

        if (snapshot.RawScore >= 0) restrictions.Add("Raw score has not dropped below zero.");

        if (snapshot.Confidence < _policy.RemovalConfidenceThreshold)
            restrictions.Add($"Confidence is below {_policy.RemovalConfidenceThreshold}.");

        if (snapshot.BadScanCount < _policy.MinimumBadScansBeforeRemoval)
            restrictions.Add($"Bad scan count is below {_policy.MinimumBadScansBeforeRemoval}.");

        if (snapshot.BadScansInLastTen < _policy.RequiredBadScansInWindow)
        {
            restrictions.Add(
                $"Bad scans in the last {_policy.RollingWindowSize} checks are below {_policy.RequiredBadScansInWindow}.");
        }

        if (snapshot.LastByteProgressAt.HasValue && now - snapshot.LastByteProgressAt.Value <
            TimeSpan.FromMinutes(_policy.ByteProgressProtectionMinutes))
            restrictions.Add("Download moved bytes recently.");

        // Counted in scans, as the setting says (it used to be applied as minutes, which at a 5 minute
        // evaluation interval protected almost nothing).
        if (snapshot.LastMeaningfulRecoveryAt.HasValue &&
            (scansSinceRecovery is null || scansSinceRecovery < _policy.RecentRecoveryProtectionScans))
            restrictions.Add("Download had recent meaningful recovery.");

        var shouldRemove = restrictions.Count == 0;

        return new HealthDecision
        {
            DownloadAttemptId = attempt.Id,
            ShouldRemove = shouldRemove,
            ShouldReplace = shouldRemove,
            ShouldProtect = snapshot.RecoveryCredit > 0,
            State = shouldRemove ? HealthState.Remove : snapshot.State,
            RawScore = snapshot.RawScore,
            VisibleScore = snapshot.VisibleScore,
            Confidence = snapshot.Confidence,
            RecoveryCredit = snapshot.RecoveryCredit,
            ReasonKey = shouldRemove ? "eligible-for-removal" : evaluation.ReasonKey,
            Message = shouldRemove ? "Download is eligible for removal and replacement." : evaluation.Message,
            Restrictions = restrictions
        };
    }

    #endregion

    #region Event Creation

    public static DownloadHealthEvent CreateHealthEvent(DownloadAttempt attempt, HealthSnapshot snapshot,
        HealthStateBefore before, HealthEvaluation evaluation, HealthObservation observation, DateTime now)
    {
        return new DownloadHealthEvent
        {
            DownloadAttempt = attempt,
            OccurredAt = now,
            EventType = evaluation.EventType,
            Severity = evaluation.Severity,
            ReasonKey = evaluation.ReasonKey,
            Message = evaluation.Message,
            ScoreBefore = before.Score,
            ScoreDelta = snapshot.RawScore - before.Score,
            ScoreAfter = snapshot.RawScore,
            ConfidenceBefore = before.Confidence,
            ConfidenceDelta = snapshot.Confidence - before.Confidence,
            ConfidenceAfter = snapshot.Confidence,
            RecoveryCreditBefore = before.RecoveryCredit,
            RecoveryCreditDelta = snapshot.RecoveryCredit - before.RecoveryCredit,
            RecoveryCreditAfter = snapshot.RecoveryCredit,
            IsGoodObservation = evaluation.IsGoodObservation,
            IsBadObservation = evaluation.IsBadObservation,
            IsMeaningfulRecovery = evaluation.IsMeaningfulRecovery,
            CountsTowardRemoval = evaluation.CountsTowardRemoval,
            Source = nameof(HealthService),
            RawStatus = observation.Status,
            RawTrackedDownloadStatus = observation.TrackedDownloadStatus,
            RawTrackedDownloadState = observation.TrackedDownloadState,
            RawErrorMessage = observation.ErrorMessage
        };
    }

    #endregion

    #region Dependencies

    private static readonly HealthEventType[] EventTypePriority =
    [
        HealthEventType.StalledNoConnections,
        HealthEventType.Stalled,
        HealthEventType.ErrorRepeated,
        HealthEventType.ErrorReported,
        HealthEventType.StatusWorsened,
        HealthEventType.WarningReported,
        HealthEventType.NoProgress,
        HealthEventType.RecoveryStarted,
        HealthEventType.SustainedProgress,
        HealthEventType.ProgressIncreased,
        HealthEventType.SizeLeftDecreased,
        HealthEventType.ErrorCleared,
        HealthEventType.RecoveryCreditApplied,
        HealthEventType.FirstSeen,
        HealthEventType.FetchingMetadata
    ];

    private readonly HealthPolicy _policy = policy ?? new HealthPolicy();

    #endregion

    #region Helpers

    private int ComputeConfidence(HealthSnapshot snapshot)
    {
        var observations = snapshot.GoodScanCount + snapshot.BadScanCount;

        if (observations == 0) return 0;

        var window = Math.Max(1, _policy.RollingWindowSize);
        var evidence = Math.Min(1m, (decimal)observations / window);
        var recentBadRatio = Math.Min(1m, (decimal)snapshot.BadScansInLastTen / window);
        var consecutiveRatio = Math.Min(1m, (decimal)snapshot.ConsecutiveBadScans / window);
        var signal = Math.Max(recentBadRatio, consecutiveRatio);

        return Math.Clamp((int)Math.Round(evidence * signal * 100), 0, 100);
    }

    public HealthState DeriveStateFromScore(decimal rawScore)
    {
        return GetState(rawScore);
    }

    private HealthState GetState(decimal rawScore)
    {
        // Remove is a decision, not a score band: HealthService sets it only when nothing blocks removal
        // (grace period, Auto Remove off, Passive Mode...). A negative score on its own is Critical.
        if (rawScore < 0) return HealthState.Critical;

        if (rawScore >= _policy.HealthyThreshold) return HealthState.Healthy;

        if (rawScore >= _policy.StableThreshold) return HealthState.Stable;

        if (rawScore >= _policy.WatchingThreshold) return HealthState.Watching;

        if (rawScore >= _policy.DegradingThreshold) return HealthState.Degrading;

        return HealthState.Critical;
    }

    private static decimal StreakMultiplier(int streak, decimal escalation, decimal cap)
    {
        if (streak <= 1) return 1m;

        return Math.Min(cap, 1m + (streak - 1) * escalation);
    }

    private static HealthEventType SelectEventType(List<HealthEventType> eventTypes)
    {
        foreach (var type in EventTypePriority)
        {
            if (eventTypes.Contains(type))
                return type;
        }

        return HealthEventType.QueueSeen;
    }

    private static string BuildReasonKey(HealthObservation observation, List<HealthEventType> eventTypes)
    {
        if (observation.IsFetchingMetadata) return "stalled-fetching-metadata";

        if (observation.IsStalledNoConnections) return "stalled-no-connections";

        if (observation.IsStalled) return "stalled";

        if (observation.IsRepeatedError) return "repeated-error";

        if (observation.HasError) return observation.ErrorKey ?? "download-error";

        if (observation.HasWarning) return "download-warning";

        if (observation.IsMeaningfulRecovery) return "meaningful-recovery";

        if (observation.HasProgressIncrease || observation.HasSizeLeftDecrease) return "progress-detected";

        if (observation.HasNoProgress) return "no-progress";

        return eventTypes.Count == 0 ? "health-observed" : NormalizeTitle(eventTypes.Last().ToString());
    }

    private static decimal? CalculateProgress(long size, long sizeLeft)
    {
        if (size <= 0) return null;

        var downloaded = Math.Max(0, size - sizeLeft);

        return Math.Round((decimal)downloaded / size * 100, 2);
    }

    public static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var chars = value.Trim().ToLowerInvariant()
            .Where(e => char.IsLetterOrDigit(e) || char.IsWhiteSpace(e) || e == '-' || e == '_')
            .Select(e => char.IsWhiteSpace(e) ? '-' : e).ToArray();

        var normalized = new string(chars);

        while (normalized.Contains("--")) normalized = normalized.Replace("--", "-");

        return normalized.Trim('-');
    }

    private static string? NormalizeError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var normalized = NormalizeTitle(value);

        if (normalized.Contains("no-connections") || normalized.Contains("no-peers")) return "stalled-no-connections";

        if (normalized.Contains("stalled")) return "stalled";

        if (normalized.Contains("not-found")) return "not-found";

        if (normalized.Contains("unpack")) return "unpack-failed";

        if (normalized.Contains("import")) return "import-failed";

        return normalized.Length > 255 ? normalized[..255] : normalized;
    }

    private static bool IsUsenet(HealthObservation observation)
    {
        return string.Equals(observation.Protocol, "usenet", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string? value, params string[] terms)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}

#region Supporting Types

public readonly record struct HealthStateBefore(decimal Score, int Confidence, int RecoveryCredit);

public sealed class HealthEvaluation
{
    public decimal ScoreDelta { get; set; }
    public int RecoveryCreditDelta { get; set; }
    public HealthEventType EventType { get; set; }
    public string ReasonKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public bool IsGoodObservation { get; set; }
    public bool IsBadObservation { get; set; }
    public bool IsMeaningfulRecovery { get; set; }
    public bool CountsTowardRemoval { get; set; }
    public int BadScansInWindow { get; set; }
    public int GoodScansInWindow { get; set; }
}

#endregion
