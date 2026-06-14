using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Data;

namespace Trackarr.Services.Media;

public class HealthService(IDbContextFactory<AppDbContext> dbFactory) : IHealthService {

    private static readonly HealthEventType[] TerminalEventTypes = {
        HealthEventType.QueueMissing,
        HealthEventType.RemovedByTrackarr,
        HealthEventType.ImportedSuccessfully,
        HealthEventType.FailedExternally
    };

    private static readonly HealthEventType[] EventTypePriority = {
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
        HealthEventType.FirstSeen
    };

    private readonly HealthPolicy _policy = new();

    public async Task<HealthDecision> EvaluateAsync(DownloadItem item, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        DateTime now = DateTime.UtcNow;

        MediaServer? server = await db.MediaServers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == item.ServerId, cancellationToken);

        DownloadAttempt attempt = await GetOrCreateAttemptAsync(db,
            item,
            server,
            now,
            cancellationToken);
        HealthSnapshot snapshot = await GetOrCreateSnapshotAsync(db, attempt, now, cancellationToken);
        MediaHealthSummary? mediaSummary = await GetMediaSummaryAsync(db, attempt, cancellationToken);

        HealthObservation observation = BuildObservation(item, attempt, snapshot, now);
        HealthEvaluation evaluation = await EvaluateObservationAsync(db,
            attempt,
            snapshot,
            mediaSummary,
            observation,
            cancellationToken);

        HealthStateBefore before = new(snapshot.RawScore, snapshot.Confidence, snapshot.RecoveryCredit);

        ApplyEvaluation(snapshot, evaluation, now);

        attempt.LastSeenAt = now;
        attempt.Title = item.Title ?? attempt.Title;
        attempt.NormalizedTitle = NormalizeTitle(item.Title ?? attempt.Title);
        attempt.DownloadClient = item.DownloadClient;
        attempt.Indexer = item.Indexer;
        attempt.Protocol = item.Protocol;
        attempt.RemoteId = item.RemoteId;

        DownloadHealthEvent healthEvent = CreateHealthEvent(attempt,
            snapshot,
            before,
            evaluation,
            observation,
            now);

        db.HealthEvents.Add(healthEvent);

        HealthDecision decision = BuildDecision(attempt, snapshot, evaluation, now);

        snapshot.IsEligibleForRemoval = decision.ShouldRemove;
        snapshot.IsRemovalRestricted = decision.Restrictions.Count > 0;
        snapshot.RemovalRestrictionReason = decision.Restrictions.Count == 0 ? null : string.Join("; ", decision.Restrictions);

        await UpdateMediaSummaryAsync(db,
            attempt,
            decision,
            now,
            cancellationToken);
        await UpdateSourceSummaryAsync(db,
            attempt,
            snapshot,
            healthEvent,
            now,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        decision.DownloadAttemptId = attempt.Id;

        return decision;
    }

    public async Task<DownloadAttempt?> GetAttemptAsync(int attemptId, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking().Include(e => e.HealthSnapshot).FirstOrDefaultAsync(e => e.Id == attemptId, cancellationToken);
    }

    public async Task<HealthSnapshot?> GetSnapshotAsync(int attemptId, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.HealthSnapshots.AsNoTracking().FirstOrDefaultAsync(e => e.DownloadAttemptId == attemptId, cancellationToken);
    }

    public async Task<List<DownloadHealthEvent>> GetEventsAsync(int attemptId, int take = 100, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.HealthEvents.AsNoTracking().Where(e => e.DownloadAttemptId == attemptId).OrderByDescending(e => e.OccurredAt).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<List<DownloadAttempt>> GetHistoryAsync(int take = 100, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking().Include(e => e.HealthSnapshot).OrderByDescending(e => e.LastSeenAt).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<List<DownloadAttempt>> GetActiveAttemptsAsync(CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking().Include(e => e.HealthSnapshot).Where(e => e.Status == AttemptStatus.Active).OrderByDescending(e => e.LastSeenAt).ToListAsync(cancellationToken);
    }

    public async Task MarkImportedAsync(int attemptId, string? message = null, CancellationToken cancellationToken = default) {
        await MarkTerminalAsync(attemptId,
            AttemptStatus.Imported,
            HealthEventType.ImportedSuccessfully,
            Severity.Positive,
            "imported-successfully",
            message ?? "Download imported successfully.",
            true,
            cancellationToken);
    }

    public async Task MarkRemovedByTrackarrAsync(int attemptId, string? message = null, CancellationToken cancellationToken = default) {
        await MarkTerminalAsync(attemptId,
            AttemptStatus.RemovedByTrackarr,
            HealthEventType.RemovedByTrackarr,
            Severity.Critical,
            "removed-by-trackarr",
            message ?? "Download removed by Trackarr.",
            false,
            cancellationToken);
    }

    public async Task MarkFailedAsync(int attemptId, string reasonKey, string message, CancellationToken cancellationToken = default) {
        await MarkTerminalAsync(attemptId,
            AttemptStatus.Failed,
            HealthEventType.FailedExternally,
            Severity.Severe,
            reasonKey,
            message,
            false,
            cancellationToken);
    }

    public async Task<List<DownloadAttempt>> ResolveMissingAttemptsAsync(int serverId, IEnumerable<string> activeDownloadIds, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        DateTime now = DateTime.UtcNow;
        HashSet<string> active = activeDownloadIds.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<DownloadAttempt> missing = await db.DownloadAttempts.Include(e => e.HealthSnapshot).Where(e => e.ServerId == serverId).Where(e => e.Status == AttemptStatus.Active).Where(e => !active.Contains(e.DownloadId)).ToListAsync(cancellationToken);

        foreach (DownloadAttempt attempt in missing) {
            HealthSnapshot snapshot = attempt.HealthSnapshot ?? await GetOrCreateSnapshotAsync(db, attempt, now, cancellationToken);

            attempt.Status = AttemptStatus.Disappeared;
            attempt.FailedAt = now;
            attempt.FinalReasonKey = "queue-missing";
            attempt.FinalMessage = "Download disappeared from the queue before Trackarr marked it as complete.";

            snapshot.State = HealthState.Unknown;
            snapshot.LastReasonKey = "queue-missing";
            snapshot.LastReasonMessage = attempt.FinalMessage;
            snapshot.UpdatedAt = now;

            db.HealthEvents.Add(new DownloadHealthEvent {
                DownloadAttempt = attempt,
                OccurredAt = now,
                EventType = HealthEventType.QueueMissing,
                Severity = Severity.Warning,
                ReasonKey = "queue-missing",
                Message = attempt.FinalMessage,
                ScoreBefore = snapshot.RawScore,
                ScoreDelta = 0,
                ScoreAfter = snapshot.RawScore,
                ConfidenceBefore = snapshot.Confidence,
                ConfidenceDelta = 0,
                ConfidenceAfter = snapshot.Confidence,
                RecoveryCreditBefore = snapshot.RecoveryCredit,
                RecoveryCreditDelta = 0,
                RecoveryCreditAfter = snapshot.RecoveryCredit,
                IsBadObservation = true,
                CountsTowardRemoval = false,
                Source = nameof(HealthService)
            });

            await UpdateMediaSummaryAsync(db,
                attempt,
                null,
                now,
                cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return missing;
    }

    private static decimal StreakMultiplier(int streak, decimal escalation, decimal cap) {
        if (streak <= 1) {
            return 1m;
        }

        return Math.Min(cap, 1m + (streak - 1) * escalation);
    }

    public async Task<List<DownloadHealthEvent>> GetEventsForItemAsync(DownloadItem item, int take = 100, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        string downloadId = GetDownloadId(item);

        int? attemptId = await db.DownloadAttempts.AsNoTracking().Where(e => e.ServerId == item.ServerId && e.DownloadId == downloadId && e.Status == AttemptStatus.Active).Select(e => (int?)e.Id).FirstOrDefaultAsync(cancellationToken);

        if (attemptId is null) {
            return [];
        }

        return await db.HealthEvents.AsNoTracking().Where(e => e.DownloadAttemptId == attemptId).OrderByDescending(e => e.OccurredAt).Take(take).ToListAsync(cancellationToken);
    }
    public async Task<HealthSnapshot?> GetMostRecentSnapshotAsync(DownloadItem item, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        string downloadId = GetDownloadId(item);

        return await db.DownloadAttempts.AsNoTracking().Where(e => e.ServerId == item.ServerId && e.DownloadId == downloadId).OrderByDescending(e => e.LastSeenAt).Select(e => e.HealthSnapshot).FirstOrDefaultAsync(cancellationToken);
    }
    public async Task<MediaHealthSummary?> GetMediaSummary(DownloadItem item, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        int remoteMovieId = item.MovieId;
        if (remoteMovieId > 0) {
            int movieId = await db.Movies.AsNoTracking().Where(e => e.ServerId == item.ServerId && e.RemoteId == remoteMovieId).Select(e => e.Id).FirstOrDefaultAsync(cancellationToken);

            if (movieId <= 0) {
                return null;
            }

            return await db.MediaHealthSummaries.AsNoTracking().Where(e => e.MovieId == movieId).OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        }

        int remoteSeriesId = item.SeriesId;
        if (remoteSeriesId > 0) {
            int seriesId = await db.Series.AsNoTracking().Where(e => e.ServerId == item.ServerId && e.RemoteId == remoteSeriesId).Select(e => e.Id).FirstOrDefaultAsync(cancellationToken);
            if (seriesId <= 0) {
                return null;
            }
            return await db.MediaHealthSummaries.AsNoTracking().Where(e => e.SeriesId == seriesId).OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    public async Task<List<DownloadAttempt>> GetDownloadAttemptsAsync(DownloadItem item, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking().Where(x => x.ServerId == item.ServerId && x.DownloadId == item.DownloadId).OrderByDescending(x => x.FirstSeenAt).ToListAsync(cancellationToken);
    }

    private async Task<DownloadAttempt> GetOrCreateAttemptAsync(
        AppDbContext db,
        DownloadItem item,
        MediaServer? server,
        DateTime now,
        CancellationToken cancellationToken) {
        string downloadId = GetDownloadId(item);

        DownloadAttempt? attempt = await db.DownloadAttempts.Include(e => e.HealthSnapshot).FirstOrDefaultAsync(e => e.ServerId == item.ServerId && e.DownloadId == downloadId && e.Status == AttemptStatus.Active, cancellationToken);

        if (attempt is not null) {
            return attempt;
        }

        TrackedMediaType mediaType = GetMediaType(item);
        int replacementAttemptNumber = await GetReplacementAttemptNumberAsync(db, item, mediaType, cancellationToken);
        int? localMovieId = item.MovieId is > 0 ? await db.Movies.AsNoTracking().Where(e => e.RemoteId == item.MovieId).Select(e => (int?)e.Id).FirstOrDefaultAsync(cancellationToken) : null;
        int? localSeriesId = item.SeriesId is > 0 ? await db.Series.AsNoTracking().Where(e => e.RemoteId == item.SeriesId).Select(e => (int?)e.Id).FirstOrDefaultAsync(cancellationToken) : null;
        int? localEpisodeId = item.EpisodeId is > 0 ? await db.Episodes.AsNoTracking().Where(e => e.RemoteId == item.EpisodeId).Select(e => (int?)e.Id).FirstOrDefaultAsync(cancellationToken) : null;

        attempt = new DownloadAttempt {
            ServerId = item.ServerId,
            ServiceType = server?.ServiceType ?? InferServiceType(item),
            DownloadId = downloadId,
            RemoteId = item.RemoteId,
            Title = item.Title ?? "Unknown download",
            NormalizedTitle = NormalizeTitle(item.Title),
            ReleaseKey = BuildReleaseKey(item),
            DownloadClient = item.DownloadClient,
            Indexer = item.Indexer,
            Protocol = item.Protocol,
            MediaType = mediaType,
            MovieId = localMovieId,
            SeriesId = localSeriesId,
            EpisodeId = localEpisodeId,
            FirstSeenAt = now,
            LastSeenAt = now,
            Status = AttemptStatus.Active,
            ReplacementAttemptNumber = replacementAttemptNumber
        };

        db.DownloadAttempts.Add(attempt);

        return attempt;
    }

    private async Task<HealthSnapshot> GetOrCreateSnapshotAsync(AppDbContext db, DownloadAttempt attempt, DateTime now, CancellationToken cancellationToken) {
        if (attempt.HealthSnapshot is not null) {
            return attempt.HealthSnapshot;
        }

        HealthSnapshot? snapshot = await db.HealthSnapshots.FirstOrDefaultAsync(e => e.DownloadAttemptId == attempt.Id, cancellationToken);

        if (snapshot is not null) {
            attempt.HealthSnapshot = snapshot;
            return snapshot;
        }

        snapshot = new HealthSnapshot {
            DownloadAttempt = attempt,
            RawScore = _policy.StartingScore,
            VisibleScore = _policy.StartingScore,
            Confidence = 0,
            RecoveryCredit = 0,
            State = HealthState.New,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.HealthSnapshots.Add(snapshot);
        attempt.HealthSnapshot = snapshot;

        return snapshot;
    }

    private async Task<MediaHealthSummary?> GetMediaSummaryAsync(AppDbContext db, DownloadAttempt attempt, CancellationToken cancellationToken) {
        return attempt.MediaType switch {
            TrackedMediaType.Movie when attempt.MovieId.HasValue => await db.MediaHealthSummaries.FirstOrDefaultAsync(e => e.MediaType == TrackedMediaType.Movie && e.MovieId == attempt.MovieId, cancellationToken),
            TrackedMediaType.Series when attempt.SeriesId.HasValue => await db.MediaHealthSummaries.FirstOrDefaultAsync(e => e.MediaType == TrackedMediaType.Series && e.SeriesId == attempt.SeriesId, cancellationToken),
            TrackedMediaType.Episode when attempt.EpisodeId.HasValue => await db.MediaHealthSummaries.FirstOrDefaultAsync(e => e.MediaType == TrackedMediaType.Episode && e.EpisodeId == attempt.EpisodeId, cancellationToken),
            _ => null
        };
    }

    private async Task<HealthEvaluation> EvaluateObservationAsync(
        AppDbContext db,
        DownloadAttempt attempt,
        HealthSnapshot snapshot,
        MediaHealthSummary? mediaSummary,
        HealthObservation observation,
        CancellationToken cancellationToken) {
        if (snapshot.LastObservationAt is null) {
            return new HealthEvaluation {
                EventType = HealthEventType.FirstSeen,
                ReasonKey = "first-seen",
                Message = "First health observation; establishing baseline.",
                Severity = Severity.Info,
                BadScansInWindow = 0,
                GoodScansInWindow = 0
            };
        }

        int badStreak = snapshot.ConsecutiveBadScans + (observation.IsBadObservation ? 1 : 0);
        int goodStreak = snapshot.ConsecutiveGoodScans + (observation.IsGoodObservation ? 1 : 0);

        decimal penaltyMultiplier = StreakMultiplier(badStreak, _policy.PenaltyStreakEscalation, _policy.PenaltyStreakCap);
        decimal rewardMultiplier = StreakMultiplier(goodStreak, _policy.RewardStreakEscalation, _policy.RewardStreakCap);

        int recoveryCreditDelta = 0;

        List<string> reasons = [];
        List<HealthEventType> eventTypes = [];
        Severity severity = Severity.Info;

        decimal coreReward = 0;
        decimal flatReward = 0;

        if (observation.HasProgressIncrease) {
            coreReward += _policy.ProgressIncreaseReward;
            reasons.Add("Progress increased.");
            eventTypes.Add(HealthEventType.ProgressIncreased);
            severity = Severity.Positive;
        }

        if (observation.HasSizeLeftDecrease) {
            coreReward += _policy.SizeLeftDecreaseReward;
            reasons.Add("Size left decreased.");
            eventTypes.Add(HealthEventType.SizeLeftDecreased);
            severity = Severity.Positive;
        }

        if (observation.HasErrorCleared) {
            flatReward += _policy.ErrorClearedReward;
            reasons.Add("Error cleared.");
            eventTypes.Add(HealthEventType.ErrorCleared);
            severity = Severity.Positive;
        }

        if (observation.IsMeaningfulRecovery) {
            flatReward += _policy.RecoveryReward;
            recoveryCreditDelta += 10;
            reasons.Add("Download recovered after bad behavior.");
            eventTypes.Add(HealthEventType.RecoveryStarted);
            severity = Severity.Positive;
        }

        decimal corePenalty = 0;
        decimal flatPenalty = 0;

        if (observation.IsStalledNoConnections) {
            corePenalty += _policy.StalledNoConnectionsPenalty;
            reasons.Add("Download is stalled with no connections.");
            eventTypes.Add(HealthEventType.StalledNoConnections);
            severity = Severity.Critical;
        }
        else if (observation.IsStalled) {
            corePenalty += _policy.StalledPenalty;
            reasons.Add("Download appears stalled.");
            eventTypes.Add(HealthEventType.Stalled);
            severity = Severity.Severe;
        }
        else if (observation.HasError) {
            corePenalty += _policy.ErrorPenalty;
            reasons.Add("Error reported.");
            eventTypes.Add(HealthEventType.ErrorReported);
            severity = Severity.Severe;
        }
        else if (observation.HasWarning) {
            corePenalty += _policy.WarningPenalty;
            reasons.Add("Warning reported.");
            eventTypes.Add(HealthEventType.WarningReported);
            severity = Severity.Warning;
        }
        else if (observation.HasNoProgress) {
            corePenalty += _policy.NoProgressPenalty;
            reasons.Add(badStreak >= 6 ? "No progress for an extended period." : badStreak >= 3 ? "Repeated lack of progress." : "No progress detected.");
            eventTypes.Add(HealthEventType.NoProgress);
            severity = Severity.Negative;
        }

        if (observation.IsRepeatedError) {
            flatPenalty += _policy.RepeatedErrorPenalty;
            reasons.Add("Same error repeated.");
            eventTypes.Add(HealthEventType.ErrorRepeated);
        }

        if (observation.IsBadObservation && mediaSummary is { QualityDowngradeCount: > 0 }) {
            flatPenalty += _policy.QualityDowngradedStillFailingPenalty;
            reasons.Add("Quality was downgraded and the download is still failing.");
            eventTypes.Add(HealthEventType.StatusWorsened);
            severity = Severity.Critical;
        }
        else if (observation.IsBadObservation && mediaSummary is { FailureCount: > 0 }) {
            flatPenalty += _policy.PreviousMediaFailurePenalty;
            reasons.Add("This media has previous failed attempts.");
            eventTypes.Add(HealthEventType.StatusWorsened);
        }

        if (observation.HasSustainedProgress) {
            flatReward += _policy.SustainedProgressReward;
            recoveryCreditDelta += 3;
            reasons.Add("Sustained progress detected.");
            eventTypes.Add(HealthEventType.SustainedProgress);
            severity = Severity.Positive;
        }

        decimal reward = coreReward * rewardMultiplier + flatReward;
        decimal penalty = corePenalty * penaltyMultiplier + flatPenalty;

        if (penalty < 0 && snapshot.RecoveryCredit > 0) {
            int absorbed = Math.Min(snapshot.RecoveryCredit, (int)Math.Ceiling(Math.Abs(penalty)));
            penalty += absorbed;
            recoveryCreditDelta -= absorbed;
            reasons.Add($"Recovery credit absorbed {absorbed} penalty points.");
            eventTypes.Add(HealthEventType.RecoveryCreditApplied);
        }

        decimal scoreDelta = reward + penalty;

        List<DownloadHealthEvent> recentWindow = attempt.Id == 0 ? [] : await db.HealthEvents.AsNoTracking().Where(e => e.DownloadAttemptId == attempt.Id).Where(e => !TerminalEventTypes.Contains(e.EventType)).OrderByDescending(e => e.OccurredAt).Take(Math.Max(0, _policy.RollingWindowSize - 1)).ToListAsync(cancellationToken);

        int badInWindow = recentWindow.Count(e => e.IsBadObservation);
        int goodInWindow = recentWindow.Count(e => e.IsGoodObservation);

        if (observation.IsBadObservation) {
            badInWindow++;
        }

        if (observation.IsGoodObservation) {
            goodInWindow++;
        }

        if (reasons.Count == 0) {
            reasons.Add("No meaningful health change detected.");
            eventTypes.Add(HealthEventType.QueueSeen);
        }

        return new HealthEvaluation {
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

    private void ApplyEvaluation(HealthSnapshot snapshot, HealthEvaluation evaluation, DateTime now) {
        decimal previousScore = snapshot.RawScore;
        int previousRecoveryCredit = snapshot.RecoveryCredit;

        snapshot.RawScore = Math.Clamp(previousScore + evaluation.ScoreDelta, -_policy.MaximumScore, _policy.MaximumScore);
        snapshot.VisibleScore = Math.Clamp((int)Math.Round(snapshot.RawScore), _policy.MinimumVisibleScore, _policy.MaximumScore);
        snapshot.RecoveryCredit = Math.Clamp(previousRecoveryCredit + evaluation.RecoveryCreditDelta, 0, _policy.RecoveryCreditMax);

        snapshot.LastScoreChange = snapshot.RawScore - previousScore;

        if (snapshot.LastScoreChange != 0) {
            snapshot.LastScoreChangedAt = now;
        }

        if (evaluation.IsGoodObservation) {
            snapshot.GoodScanCount++;
            snapshot.ConsecutiveGoodScans++;
            snapshot.ConsecutiveBadScans = 0;
            snapshot.LastGoodObservationAt = now;
        }

        if (evaluation.IsBadObservation) {
            snapshot.BadScanCount++;
            snapshot.ConsecutiveBadScans++;
            snapshot.ConsecutiveGoodScans = 0;
            snapshot.LastBadObservationAt = now;
        }

        if (!evaluation.IsGoodObservation && !evaluation.IsBadObservation) {
            snapshot.ConsecutiveGoodScans = 0;
            snapshot.ConsecutiveBadScans = 0;
        }

        if (evaluation.IsMeaningfulRecovery) {
            snapshot.LastMeaningfulRecoveryAt = now;
        }

        snapshot.BadScansInLastTen = evaluation.BadScansInWindow;
        snapshot.GoodScansInLastTen = evaluation.GoodScansInWindow;
        snapshot.Confidence = ComputeConfidence(snapshot);
        snapshot.State = GetState(snapshot.RawScore);
        snapshot.LastReasonKey = evaluation.ReasonKey;
        snapshot.LastReasonMessage = evaluation.Message;
        snapshot.LastObservationAt = now;
        snapshot.UpdatedAt = now;
    }

    private int ComputeConfidence(HealthSnapshot snapshot) {
        int observations = snapshot.GoodScanCount + snapshot.BadScanCount;

        if (observations == 0) {
            return 0;
        }

        int window = Math.Max(1, _policy.RollingWindowSize);
        decimal evidence = Math.Min(1m, (decimal)observations / window);
        decimal recentBadRatio = Math.Min(1m, (decimal)snapshot.BadScansInLastTen / window);
        decimal consecutiveRatio = Math.Min(1m, (decimal)snapshot.ConsecutiveBadScans / window);
        decimal signal = Math.Max(recentBadRatio, consecutiveRatio);

        return Math.Clamp((int)Math.Round(evidence * signal * 100), 0, 100);
    }

    private HealthDecision BuildDecision(DownloadAttempt attempt, HealthSnapshot snapshot, HealthEvaluation evaluation, DateTime now) {
        List<string> restrictions = [];

        TimeSpan age = now - attempt.FirstSeenAt;

        if (attempt.IsProtected) {
            restrictions.Add("Download is protected.");
        }

        if (attempt.Status != AttemptStatus.Active) {
            restrictions.Add("Download attempt is no longer active.");
        }

        if (age < TimeSpan.FromMinutes(_policy.StartupGracePeriodMinutes)) {
            restrictions.Add($"Download is inside the {_policy.StartupGracePeriodMinutes} minute startup grace period.");
        }

        if (snapshot.RawScore >= 0) {
            restrictions.Add("Raw score has not dropped below zero.");
        }

        if (snapshot.Confidence < _policy.RemovalConfidenceThreshold) {
            restrictions.Add($"Confidence is below {_policy.RemovalConfidenceThreshold}.");
        }

        if (snapshot.BadScanCount < _policy.MinimumBadScansBeforeRemoval) {
            restrictions.Add($"Bad scan count is below {_policy.MinimumBadScansBeforeRemoval}.");
        }

        if (snapshot.BadScansInLastTen < _policy.RequiredBadScansInWindow) {
            restrictions.Add($"Bad scans in the last {_policy.RollingWindowSize} checks are below {_policy.RequiredBadScansInWindow}.");
        }

        if (snapshot.LastMeaningfulRecoveryAt.HasValue) {
            DateTime protectedUntil = snapshot.LastMeaningfulRecoveryAt.Value.AddMinutes(_policy.RecentRecoveryProtectionScans);

            if (now < protectedUntil) {
                restrictions.Add("Download had recent meaningful recovery.");
            }
        }

        bool shouldRemove = restrictions.Count == 0;

        return new HealthDecision {
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

    private DownloadHealthEvent CreateHealthEvent(
        DownloadAttempt attempt,
        HealthSnapshot snapshot,
        HealthStateBefore before,
        HealthEvaluation evaluation,
        HealthObservation observation,
        DateTime now) => new() {
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

    private async Task UpdateMediaSummaryAsync(
        AppDbContext db,
        DownloadAttempt attempt,
        HealthDecision? decision,
        DateTime now,
        CancellationToken cancellationToken) {
        if (attempt.MediaType == TrackedMediaType.Unknown) {
            return;
        }

        MediaHealthSummary? summary = await GetMediaSummaryAsync(db, attempt, cancellationToken);

        if (summary is null) {
            summary = new MediaHealthSummary {
                MediaType = attempt.MediaType,
                MovieId = attempt.MovieId,
                SeriesId = attempt.SeriesId,
                EpisodeId = attempt.EpisodeId,
                CreatedAt = now
            };

            db.MediaHealthSummaries.Add(summary);
        }

        IQueryable<DownloadAttempt> attempts = db.DownloadAttempts.Where(e => e.MediaType == attempt.MediaType);

        attempts = attempt.MediaType switch {
            TrackedMediaType.Movie => attempts.Where(e => e.MovieId == attempt.MovieId),
            TrackedMediaType.Series => attempts.Where(e => e.SeriesId == attempt.SeriesId),
            TrackedMediaType.Episode => attempts.Where(e => e.EpisodeId == attempt.EpisodeId),
            _ => attempts.Where(e => false)
        };

        summary.AttemptCount = await attempts.CountAsync(cancellationToken);
        summary.ActiveAttemptCount = await attempts.CountAsync(e => e.Status == AttemptStatus.Active, cancellationToken);
        summary.SuccessCount = await attempts.CountAsync(e => e.WasSuccessful || e.Status == AttemptStatus.Imported, cancellationToken);
        summary.FailureCount = await attempts.CountAsync(e => e.Status == AttemptStatus.Failed || e.Status == AttemptStatus.Disappeared, cancellationToken);
        summary.RemovalCount = await attempts.CountAsync(e => e.Status == AttemptStatus.RemovedByTrackarr, cancellationToken);
        summary.ReplacementCount = await attempts.CountAsync(e => e.Status == AttemptStatus.Replaced, cancellationToken);

        summary.LastAttemptAt = await attempts.MaxAsync(e => (DateTime?)e.FirstSeenAt, cancellationToken);
        summary.LastSuccessAt = await attempts.Where(e => e.WasSuccessful || e.Status == AttemptStatus.Imported).MaxAsync(e => e.CompletedAt, cancellationToken);
        summary.LastFailureAt = await attempts.Where(e => e.Status == AttemptStatus.Failed || e.Status == AttemptStatus.Disappeared).MaxAsync(e => e.FailedAt, cancellationToken);
        summary.LastRemovalAt = await attempts.Where(e => e.Status == AttemptStatus.RemovedByTrackarr).MaxAsync(e => e.RemovedAt, cancellationToken);
        summary.LastReplacementAt = await attempts.Where(e => e.Status == AttemptStatus.Replaced).MaxAsync(e => e.ReplacedAt, cancellationToken);

        List<HealthSnapshot> activeSnapshots = await db
            .HealthSnapshots
            .Where(e => e.DownloadAttempt != null)
            .Where(e => e.DownloadAttempt!.MediaType == attempt.MediaType)
            .Where(e => e.DownloadAttempt!.Status == AttemptStatus.Active)
            .Where(e => attempt.MediaType == TrackedMediaType.Movie && e.DownloadAttempt!.MovieId == attempt.MovieId || attempt.MediaType == TrackedMediaType.Series && e.DownloadAttempt!.SeriesId == attempt.SeriesId || attempt.MediaType == TrackedMediaType.Episode && e.DownloadAttempt!.EpisodeId == attempt.EpisodeId)
            .ToListAsync(cancellationToken);

        summary.CurrentBestScore = activeSnapshots.Count == 0 ? 0 : activeSnapshots.Max(e => e.VisibleScore);
        summary.CurrentWorstScore = activeSnapshots.Count == 0 ? 0 : activeSnapshots.Min(e => e.VisibleScore);
        summary.CurrentState = activeSnapshots.Count == 0 ? HealthState.Unknown : activeSnapshots.OrderBy(e => e.VisibleScore).First().State;

        bool isStuck = summary.FailureCount >= 3 || summary.RemovalCount >= 3 || decision?.ShouldRemove == true;

        summary.IsStuck = isStuck;
        summary.StuckSince = isStuck ? summary.StuckSince ?? now : null;
        summary.StuckReasonKey = isStuck ? "repeated-download-failure" : null;
        summary.StuckReasonMessage = isStuck ? "Media has repeated failed or removed download attempts." : null;
        summary.UpdatedAt = now;
    }

    private async Task UpdateSourceSummaryAsync(
        AppDbContext db,
        DownloadAttempt attempt,
        HealthSnapshot snapshot,
        DownloadHealthEvent healthEvent,
        DateTime now,
        CancellationToken cancellationToken) {
        SourceHealthSummary? summary = await db.SourceHealthSummaries.FirstOrDefaultAsync(e =>
                e.ServerId == attempt.ServerId && e.ServiceType == attempt.ServiceType && e.Indexer == attempt.Indexer && e.DownloadClient == attempt.DownloadClient && e.Protocol == attempt.Protocol && e.ReleaseKey == attempt.ReleaseKey && e.ReasonKey == healthEvent.ReasonKey,
            cancellationToken);

        if (summary is null) {
            summary = new SourceHealthSummary {
                ServerId = attempt.ServerId,
                ServiceType = attempt.ServiceType,
                Indexer = attempt.Indexer,
                DownloadClient = attempt.DownloadClient,
                Protocol = attempt.Protocol,
                ReleaseKey = attempt.ReleaseKey,
                ReasonKey = healthEvent.ReasonKey,
                FirstSeenAt = now,
                CreatedAt = now,
                ReputationScore = 70
            };

            db.SourceHealthSummaries.Add(summary);
        }

        summary.AttemptCount++;
        summary.LastSeenAt = now;
        summary.AverageScore = summary.AttemptCount == 1 ? snapshot.VisibleScore : (summary.AverageScore * (summary.AttemptCount - 1) + snapshot.VisibleScore) / summary.AttemptCount;

        summary.WorstScore = summary.AttemptCount == 1 ? snapshot.VisibleScore : Math.Min(summary.WorstScore, snapshot.VisibleScore);
        summary.BestScore = summary.AttemptCount == 1 ? snapshot.VisibleScore : Math.Max(summary.BestScore, snapshot.VisibleScore);

        if (healthEvent.IsBadObservation) {
            summary.FailureCount++;
            summary.LastFailureAt = now;
            summary.ReputationScore = Math.Max(0, summary.ReputationScore - 3);
        }

        if (healthEvent.IsGoodObservation) {
            summary.SuccessCount++;
            summary.LastSuccessAt = now;
            summary.ReputationScore = Math.Min(100, summary.ReputationScore + 1);
        }

        if (healthEvent.IsMeaningfulRecovery) {
            summary.RecoveryCount++;
            summary.ReputationScore = Math.Min(100, summary.ReputationScore + 2);
        }

        if (snapshot.State == HealthState.Remove) {
            summary.RemovalCount++;
            summary.ReputationScore = Math.Max(0, summary.ReputationScore - 5);
        }

        summary.UpdatedAt = now;
    }

    private async Task MarkTerminalAsync(
        int attemptId,
        AttemptStatus status,
        HealthEventType eventType,
        Severity severity,
        string reasonKey,
        string message,
        bool success,
        CancellationToken cancellationToken) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        DateTime now = DateTime.UtcNow;

        DownloadAttempt? attempt = await db.DownloadAttempts.Include(e => e.HealthSnapshot).FirstOrDefaultAsync(e => e.Id == attemptId, cancellationToken);

        if (attempt is null) {
            return;
        }

        HealthSnapshot snapshot = attempt.HealthSnapshot ?? await GetOrCreateSnapshotAsync(db, attempt, now, cancellationToken);

        attempt.Status = status;
        attempt.FinalReasonKey = reasonKey;
        attempt.FinalMessage = message;
        attempt.WasSuccessful = success;
        attempt.WasImported = status == AttemptStatus.Imported;
        attempt.WasRemovedByTrackarr = status == AttemptStatus.RemovedByTrackarr;

        if (status == AttemptStatus.Imported) {
            attempt.CompletedAt = now;
            snapshot.RawScore = _policy.MaximumScore;
            snapshot.VisibleScore = _policy.MaximumScore;
            snapshot.State = HealthState.Resolved;
        }

        if (status == AttemptStatus.RemovedByTrackarr) {
            attempt.RemovedAt = now;
            snapshot.State = HealthState.Remove;
        }

        if (status == AttemptStatus.Failed) {
            attempt.FailedAt = now;
            snapshot.State = HealthState.Failed;
        }

        snapshot.LastReasonKey = reasonKey;
        snapshot.LastReasonMessage = message;
        snapshot.UpdatedAt = now;

        db.HealthEvents.Add(new DownloadHealthEvent {
            DownloadAttempt = attempt,
            OccurredAt = now,
            EventType = eventType,
            Severity = severity,
            ReasonKey = reasonKey,
            Message = message,
            ScoreBefore = snapshot.RawScore,
            ScoreDelta = 0,
            ScoreAfter = snapshot.RawScore,
            ConfidenceBefore = snapshot.Confidence,
            ConfidenceDelta = 0,
            ConfidenceAfter = snapshot.Confidence,
            RecoveryCreditBefore = snapshot.RecoveryCredit,
            RecoveryCreditDelta = 0,
            RecoveryCreditAfter = snapshot.RecoveryCredit,
            IsGoodObservation = success,
            IsBadObservation = !success,
            IsMeaningfulRecovery = false,
            CountsTowardRemoval = false,
            Source = nameof(HealthService)
        });

        await UpdateMediaSummaryAsync(db,
            attempt,
            null,
            now,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static HealthObservation BuildObservation(DownloadItem item, DownloadAttempt attempt, HealthSnapshot snapshot, DateTime now) {
        decimal? progress = CalculateProgress(item.Size, item.SizeLeft);
        bool isFetchingMetadata = ContainsAny(item.ErrorMessage, "downloading metadata", "fetching metadata");
        string? errorKey = isFetchingMetadata ? null : NormalizeError(item.ErrorMessage);
        bool hasError = !isFetchingMetadata && !string.IsNullOrWhiteSpace(item.ErrorMessage);
        bool hasWarning = ContainsAny(item.Status, "warn", "warning") || ContainsAny(item.TrackedDownloadStatus, "warn", "warning");
        bool isStalled = isFetchingMetadata || ContainsAny(item.Status, "stalled", "stuck") || ContainsAny(item.TrackedDownloadStatus, "stalled", "stuck") || ContainsAny(item.TrackedDownloadState, "stalled", "stuck") || ContainsAny(item.ErrorMessage, "stalled", "stuck");

        bool isStalledNoConnections = isStalled && ContainsAny(item.ErrorMessage, "no connections", "no peers", "0 peers") || isFetchingMetadata;
        bool progressIncreased = progress.HasValue && snapshot.LastProgress.HasValue && progress.Value > snapshot.LastProgress.Value;
        bool sizeLeftDecreased = snapshot.LastSizeLeft.HasValue && item.SizeLeft < snapshot.LastSizeLeft.Value;
        bool errorCleared = !hasError && !isFetchingMetadata && !string.IsNullOrWhiteSpace(snapshot.LastErrorMessage);
        bool repeatedError = !string.IsNullOrWhiteSpace(errorKey) && errorKey == snapshot.LastErrorKey;
        bool hasPreviousObservation = snapshot.LastObservationAt.HasValue;
        bool hasNoProgress = hasPreviousObservation && !progressIncreased && !sizeLeftDecreased;
        bool isGood = progressIncreased || sizeLeftDecreased || errorCleared;
        bool isBad = hasNoProgress || hasError || hasWarning || isStalled || isStalledNoConnections || repeatedError;
        bool meaningfulRecovery = isGood && (snapshot.ConsecutiveBadScans > 0 || snapshot.State is HealthState.Watching or HealthState.Degrading or HealthState.Critical);
        bool sustainedProgress = isGood && snapshot.ConsecutiveGoodScans >= 3;

        snapshot.LastProgress = progress;
        snapshot.LastSizeLeft = item.SizeLeft;
        snapshot.LastSize = item.Size;
        snapshot.LastStatus = item.Status;
        snapshot.LastTrackedDownloadStatus = item.TrackedDownloadStatus;
        snapshot.LastTrackedDownloadState = item.TrackedDownloadState;
        snapshot.LastErrorKey = errorKey;
        snapshot.LastErrorMessage = isFetchingMetadata ? null : item.ErrorMessage;

        return new HealthObservation {
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
            HasPositiveSpeed = progressIncreased || sizeLeftDecreased,
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

    private HealthState GetState(decimal rawScore) {
        if (rawScore < 0) {
            return HealthState.Remove;
        }

        if (rawScore >= _policy.HealthyThreshold) {
            return HealthState.Healthy;
        }

        if (rawScore >= _policy.StableThreshold) {
            return HealthState.Stable;
        }

        if (rawScore >= _policy.WatchingThreshold) {
            return HealthState.Watching;
        }

        if (rawScore >= _policy.DegradingThreshold) {
            return HealthState.Degrading;
        }

        return HealthState.Critical;
    }

    private static string GetDownloadId(DownloadItem item) {
        if (!string.IsNullOrWhiteSpace(item.DownloadId)) {
            return item.DownloadId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.AttemptKey)) {
            return item.AttemptKey.Trim();
        }

        return $"{item.ServerId}:{item.RemoteId}:{NormalizeTitle(item.Title)}";
    }

    private static TrackedMediaType GetMediaType(DownloadItem item) {
        if (item.MovieId > 0) {
            return TrackedMediaType.Movie;
        }
        if (item.SeriesId > 0 || item.EpisodeId > 0) {
            return TrackedMediaType.Series;
        }
        return TrackedMediaType.Unknown;
    }

    private static ServiceType InferServiceType(DownloadItem item) {
        if (item.EpisodeId > 0 || item.SeriesId > 0) {
            return ServiceType.Sonarr;
        }

        return ServiceType.Radarr;
    }

    private async Task<int> GetReplacementAttemptNumberAsync(AppDbContext db, DownloadItem item, TrackedMediaType mediaType, CancellationToken cancellationToken) {
        IQueryable<DownloadAttempt> query = db.DownloadAttempts.Where(e => e.MediaType == mediaType);

        query = mediaType switch {
            TrackedMediaType.Movie => query.Where(e => e.MovieId == item.MovieId),
            TrackedMediaType.Series => query.Where(e => e.SeriesId == item.SeriesId),
            TrackedMediaType.Episode => query.Where(e => e.EpisodeId == item.EpisodeId),
            _ => query.Where(e => false)
        };

        return await query.CountAsync(cancellationToken) + 1;
    }

    private static string BuildReleaseKey(DownloadItem item) {
        string title = NormalizeTitle(item.Title);
        string indexer = NormalizeTitle(item.Indexer);
        string protocol = NormalizeTitle(item.Protocol);

        return string.Join(":", new[] { title, indexer, protocol }.Where(e => !string.IsNullOrWhiteSpace(e)));
    }

    private static HealthEventType SelectEventType(List<HealthEventType> eventTypes) {
        foreach (HealthEventType type in EventTypePriority) {
            if (eventTypes.Contains(type)) {
                return type;
            }
        }

        return HealthEventType.QueueSeen;
    }

    private static string BuildReasonKey(HealthObservation observation, List<HealthEventType> eventTypes) {
        if (observation.IsFetchingMetadata) {
            return "stalled-fetching-metadata";
        }
        if (observation.IsStalledNoConnections) {
            return "stalled-no-connections";
        }

        if (observation.IsStalled) {
            return "stalled";
        }

        if (observation.IsRepeatedError) {
            return "repeated-error";
        }

        if (observation.HasError) {
            return observation.ErrorKey ?? "download-error";
        }

        if (observation.HasWarning) {
            return "download-warning";
        }

        if (observation.IsMeaningfulRecovery) {
            return "meaningful-recovery";
        }

        if (observation.HasProgressIncrease || observation.HasSizeLeftDecrease) {
            return "progress-detected";
        }

        if (observation.HasNoProgress) {
            return "no-progress";
        }

        return eventTypes.Count == 0 ? "health-observed" : NormalizeTitle(eventTypes.Last().ToString());
    }

    private static decimal? CalculateProgress(long size, long sizeLeft) {
        if (size <= 0) {
            return null;
        }

        long downloaded = Math.Max(0, size - sizeLeft);

        return Math.Round((decimal)downloaded / size * 100, 2);
    }

    private static string NormalizeTitle(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        return string.Join("-", value.Trim().ToLowerInvariant().Where(e => char.IsLetterOrDigit(e) || char.IsWhiteSpace(e) || e == '-' || e == '_').Select(e => char.IsWhiteSpace(e) ? '-' : e));
    }

    private static string? NormalizeError(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        string normalized = NormalizeTitle(value);

        if (normalized.Contains("no-connections") || normalized.Contains("no-peers")) {
            return "stalled-no-connections";
        }

        if (normalized.Contains("stalled")) {
            return "stalled";
        }

        if (normalized.Contains("not-found")) {
            return "not-found";
        }

        if (normalized.Contains("unpack")) {
            return "unpack-failed";
        }

        if (normalized.Contains("import")) {
            return "import-failed";
        }

        return normalized.Length > 255 ? normalized[..255] : normalized;
    }

    private static bool ContainsAny(string? value, params string[] terms) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private readonly record struct HealthStateBefore(decimal Score, int Confidence, int RecoveryCredit);

    private sealed class HealthEvaluation {
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
}