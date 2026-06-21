using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Data;

namespace Trackarr.Services.Media;

public class HealthService(IDbContextFactory<AppDbContext> dbFactory) : IHealthService
{
    #region Policy & Static Data

    private static readonly HealthEventType[] TerminalEventTypes =
    [
        HealthEventType.QueueMissing,
        HealthEventType.RemovedByTrackarr,
        HealthEventType.ImportedSuccessfully,
        HealthEventType.FailedExternally
    ];

    private readonly HealthPolicy _policy = new();
    private readonly HealthEvaluator _evaluator = new();
    private readonly HealthRepository _repository = new();

    #endregion

    #region Evaluation

    public async Task<List<DownloadAttempt>> ResolveMissingAttemptsAsync(int serverId,
        IEnumerable<string> activeDownloadIds, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var active = activeDownloadIds.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = await db.DownloadAttempts.Include(e => e.HealthSnapshot).Where(e => e.ServerId == serverId)
            .Where(e => e.Status == AttemptStatus.Active).Where(e => !active.Contains(e.DownloadId))
            .ToListAsync(cancellationToken);

        foreach (var attempt in missing)
        {
            var snapshot = attempt.HealthSnapshot ??
                           await _repository.GetOrCreateSnapshotAsync(db, attempt, now, cancellationToken);

            attempt.Status = AttemptStatus.Disappeared;
            attempt.FailedAt = now;
            attempt.FinalReasonKey = "queue-missing";
            attempt.FinalMessage = "Download disappeared from the queue before Trackarr marked it as complete.";

            snapshot.State = HealthState.Unknown;
            snapshot.LastReasonKey = "queue-missing";
            snapshot.LastReasonMessage = attempt.FinalMessage;
            snapshot.UpdatedAt = now;

            db.HealthEvents.Add(new DownloadHealthEvent
            {
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

            await _repository.UpdateMediaSummaryAsync(db, attempt, null, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return missing;
    }

    public async Task<HealthDecision> EvaluateAsync(DownloadItem item, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;

        var server = await db.MediaServers.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == item.ServerId, cancellationToken);

        var attempt = await _repository.GetOrCreateAttemptAsync(db, item, server, now, cancellationToken);
        var snapshot = await _repository.GetOrCreateSnapshotAsync(db, attempt, now, cancellationToken);
        var mediaSummary = await _repository.GetMediaSummaryAsync(db, attempt, cancellationToken);

        var observation = HealthEvaluator.BuildObservation(item, attempt, snapshot, now);

        var recentWindow = attempt.Id == 0
            ? []
            : await db.HealthEvents.AsNoTracking().Where(e => e.DownloadAttemptId == attempt.Id)
                .Where(e => !TerminalEventTypes.Contains(e.EventType)).OrderByDescending(e => e.OccurredAt)
                .Take(Math.Max(0, _policy.RollingWindowSize - 1)).ToListAsync(cancellationToken);

        var evaluation = _evaluator.Evaluate(attempt, snapshot, mediaSummary, observation,
            recentWindow.Count(e => e.IsBadObservation), recentWindow.Count(e => e.IsGoodObservation));

        HealthStateBefore before = new(snapshot.RawScore, snapshot.Confidence, snapshot.RecoveryCredit);

        _evaluator.ApplyEvaluation(snapshot, evaluation, now);

        attempt.LastSeenAt = now;
        attempt.Title = item.Title ?? attempt.Title;
        attempt.NormalizedTitle = HealthEvaluator.NormalizeTitle(item.Title ?? attempt.Title);
        attempt.DownloadClient = item.DownloadClient;
        attempt.Indexer = item.Indexer;
        attempt.Protocol = item.Protocol;
        attempt.RemoteId = item.RemoteId;

        var healthEvent = HealthEvaluator.CreateHealthEvent(attempt, snapshot, before, evaluation, observation, now);

        db.HealthEvents.Add(healthEvent);

        var decision = _evaluator.BuildDecision(attempt, snapshot, evaluation, now);

        snapshot.IsEligibleForRemoval = decision.ShouldRemove;
        snapshot.IsRemovalRestricted = decision.Restrictions.Count > 0;
        snapshot.RemovalRestrictionReason =
            decision.Restrictions.Count == 0 ? null : string.Join("; ", decision.Restrictions);

        await _repository.UpdateMediaSummaryAsync(db, attempt, decision, now, cancellationToken);
        await _repository.UpdateSourceSummaryAsync(db, attempt, snapshot, healthEvent, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        decision.DownloadAttemptId = attempt.Id;

        return decision;
    }

    #endregion

    #region Lifecycle & Reconciliation

    public async Task MarkImportedAsync(int attemptId, string? message = null,
        CancellationToken cancellationToken = default)
    {
        await MarkTerminalAsync(attemptId, AttemptStatus.Imported, HealthEventType.ImportedSuccessfully,
            Severity.Positive, "imported-successfully", message ?? "Download imported successfully.", true,
            cancellationToken);
    }

    public async Task MarkRemovedByTrackarrAsync(int attemptId, string? message = null,
        CancellationToken cancellationToken = default)
    {
        await MarkTerminalAsync(attemptId, AttemptStatus.RemovedByTrackarr, HealthEventType.RemovedByTrackarr,
            Severity.Critical, "removed-by-trackarr", message ?? "Download removed by Trackarr.", false,
            cancellationToken);
    }

    public async Task MarkFailedAsync(int attemptId, string reasonKey, string message,
        CancellationToken cancellationToken = default)
    {
        await MarkTerminalAsync(attemptId, AttemptStatus.Failed, HealthEventType.FailedExternally, Severity.Severe,
            reasonKey, message, false, cancellationToken);
    }

    public async Task<List<DownloadAttempt>> ResolveMissingAttemptsAsync(int serverId,
        IEnumerable<string> activeDownloadIds, IEnumerable<string>? importedDownloadIds = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var active = activeDownloadIds.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = (importedDownloadIds ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = await db.DownloadAttempts.Include(e => e.HealthSnapshot).Where(e => e.ServerId == serverId)
            .Where(e => e.Status == AttemptStatus.Active).Where(e => !active.Contains(e.DownloadId))
            .ToListAsync(cancellationToken);

        foreach (var attempt in missing)
        {
            var snapshot = attempt.HealthSnapshot ??
                           await _repository.GetOrCreateSnapshotAsync(db, attempt, now, cancellationToken);

            if (imported.Contains(attempt.DownloadId))
            {
                attempt.Status = AttemptStatus.Imported;
                attempt.WasSuccessful = true;
                attempt.WasImported = true;
                attempt.CompletedAt = now;
                attempt.FinalReasonKey = "imported-successfully";
                attempt.FinalMessage = "Download imported successfully.";

                snapshot.RawScore = _policy.MaximumScore;
                snapshot.VisibleScore = _policy.MaximumScore;
                snapshot.State = HealthState.Resolved;
                snapshot.LastReasonKey = "imported-successfully";
                snapshot.LastReasonMessage = attempt.FinalMessage;
                snapshot.UpdatedAt = now;

                db.HealthEvents.Add(new DownloadHealthEvent
                {
                    DownloadAttempt = attempt,
                    OccurredAt = now,
                    EventType = HealthEventType.ImportedSuccessfully,
                    Severity = Severity.Positive,
                    ReasonKey = "imported-successfully",
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
                    IsGoodObservation = true,
                    CountsTowardRemoval = false,
                    Source = nameof(HealthService)
                });
            }
            else
            {
                attempt.Status = AttemptStatus.Disappeared;
                attempt.FailedAt = now;
                attempt.FinalReasonKey = "queue-missing";
                attempt.FinalMessage = "Download disappeared from the queue before Trackarr marked it as complete.";

                snapshot.State = HealthState.Unknown;
                snapshot.LastReasonKey = "queue-missing";
                snapshot.LastReasonMessage = attempt.FinalMessage;
                snapshot.UpdatedAt = now;

                db.HealthEvents.Add(new DownloadHealthEvent
                {
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
            }

            await _repository.UpdateMediaSummaryAsync(db, attempt, null, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return missing;
    }

    private async Task MarkTerminalAsync(int attemptId, AttemptStatus status, HealthEventType eventType,
        Severity severity, string reasonKey, string message, bool success, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;

        var attempt = await db.DownloadAttempts.Include(e => e.HealthSnapshot)
            .FirstOrDefaultAsync(e => e.Id == attemptId, cancellationToken);

        if (attempt is null) return;

        var snapshot = attempt.HealthSnapshot ??
                       await _repository.GetOrCreateSnapshotAsync(db, attempt, now, cancellationToken);

        attempt.Status = status;
        attempt.FinalReasonKey = reasonKey;
        attempt.FinalMessage = message;
        attempt.WasSuccessful = success;
        attempt.WasImported = status == AttemptStatus.Imported;
        attempt.WasRemovedByTrackarr = status == AttemptStatus.RemovedByTrackarr;

        if (status == AttemptStatus.Imported)
        {
            attempt.CompletedAt = now;
            snapshot.RawScore = _policy.MaximumScore;
            snapshot.VisibleScore = _policy.MaximumScore;
            snapshot.State = HealthState.Resolved;
        }

        if (status == AttemptStatus.RemovedByTrackarr)
        {
            attempt.RemovedAt = now;
            snapshot.State = HealthState.Remove;
        }

        if (status == AttemptStatus.Failed)
        {
            attempt.FailedAt = now;
            snapshot.State = HealthState.Failed;
        }

        snapshot.LastReasonKey = reasonKey;
        snapshot.LastReasonMessage = message;
        snapshot.UpdatedAt = now;

        db.HealthEvents.Add(new DownloadHealthEvent
        {
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

        await _repository.UpdateMediaSummaryAsync(db, attempt, null, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region Queries

    public async Task<DownloadAttempt?> GetAttemptAsync(int attemptId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking().Include(e => e.HealthSnapshot)
            .FirstOrDefaultAsync(e => e.Id == attemptId, cancellationToken);
    }

    public async Task<HealthSnapshot?> GetSnapshotAsync(int attemptId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.HealthSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(e => e.DownloadAttemptId == attemptId, cancellationToken);
    }

    public async Task<List<DownloadHealthEvent>> GetEventsAsync(int attemptId, int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.HealthEvents.AsNoTracking().Where(e => e.DownloadAttemptId == attemptId)
            .OrderByDescending(e => e.OccurredAt).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<List<DownloadAttempt>> GetHistoryAsync(int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking().Include(e => e.HealthSnapshot)
            .OrderByDescending(e => e.LastSeenAt).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<List<DownloadAttempt>> GetActiveAttemptsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking().Include(e => e.HealthSnapshot)
            .Where(e => e.Status == AttemptStatus.Active).OrderByDescending(e => e.LastSeenAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetActiveDownloadIdsAsync(int serverId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking()
            .Where(e => e.ServerId == serverId && e.Status == AttemptStatus.Active).Select(e => e.DownloadId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<DownloadHealthEvent>> GetEventsForItemAsync(DownloadItem item, int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var downloadId = GetDownloadId(item);

        var attemptId = await db.DownloadAttempts.AsNoTracking()
            .Where(e => e.ServerId == item.ServerId && e.DownloadId == downloadId && e.Status == AttemptStatus.Active)
            .Select(e => (int?)e.Id).FirstOrDefaultAsync(cancellationToken);

        if (attemptId is null) return [];

        return await db.HealthEvents.AsNoTracking().Where(e => e.DownloadAttemptId == attemptId)
            .OrderByDescending(e => e.OccurredAt).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<HealthSnapshot?> GetMostRecentSnapshotAsync(DownloadItem item,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var downloadId = GetDownloadId(item);

        return await db.DownloadAttempts.AsNoTracking()
            .Where(e => e.ServerId == item.ServerId && e.DownloadId == downloadId).OrderByDescending(e => e.LastSeenAt)
            .Select(e => e.HealthSnapshot).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<HealthSnapshot?> GetMostRecentSnapshotAsync(int serverId, int? movieRemoteId, int? seriesRemoteId,
        int? episodeRemoteId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var query = ApplyMediaAttemptFilter(db.DownloadAttempts.AsNoTracking().Where(e => e.ServerId == serverId),
            movieRemoteId, seriesRemoteId, episodeRemoteId);

        return await query.OrderByDescending(e => e.LastSeenAt).Select(e => e.HealthSnapshot)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<List<DownloadHealthEvent>> GetEventsForMediaAsync(int serverId, int? movieRemoteId, int? seriesRemoteId,
        int? episodeRemoteId)
    {
        return GetEventsForMediaAsync(serverId, movieRemoteId, seriesRemoteId, episodeRemoteId, CancellationToken.None);
    }

    public async Task<List<DownloadHealthEvent>> GetEventsForMediaAsync(int serverId, int? movieRemoteId,
        int? seriesRemoteId, int? episodeRemoteId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var query = ApplyMediaAttemptFilter(db.DownloadAttempts.AsNoTracking().Where(e => e.ServerId == serverId),
            movieRemoteId, seriesRemoteId, episodeRemoteId);

        return await query.SelectMany(e => e.HealthEvents).OrderByDescending(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<DownloadAttempt>> GetDownloadAttemptsAsync(int serverId, int? movieRemoteId,
        int? seriesRemoteId, int? episodeRemoteId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var query = ApplyMediaAttemptFilter(db.DownloadAttempts.AsNoTracking().Where(e => e.ServerId == serverId),
            movieRemoteId, seriesRemoteId, episodeRemoteId);

        return await query.OrderByDescending(e => e.FirstSeenAt).ToListAsync(cancellationToken);
    }

    private static IQueryable<DownloadAttempt> ApplyMediaAttemptFilter(IQueryable<DownloadAttempt> query,
        int? movieRemoteId, int? seriesRemoteId, int? episodeRemoteId)
    {
        if (episodeRemoteId is > 0)
            return query.Where(e => e.EpisodeId == episodeRemoteId.Value);

        if (movieRemoteId is > 0)
            return query.Where(e => e.MovieId == movieRemoteId.Value);

        if (seriesRemoteId is > 0)
            return query.Where(e => e.SeriesId == seriesRemoteId.Value);

        return query.Where(e => false);
    }

    public async Task<MediaHealthSummary?> GetMediaSummary(int serverId, int? movieRemoteId, int? seriesRemoteId,
        int? episodeRemoteId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (episodeRemoteId is > 0)
        {
            var episode = await db.Episodes.AsNoTracking()
                .Where(e => e.ServerId == serverId && e.RemoteId == episodeRemoteId.Value)
                .Select(e => new { e.Id, e.RemoteSeriesId }).FirstOrDefaultAsync(cancellationToken);

            if (episode != null)
            {
                var summary = await db.MediaHealthSummaries.AsNoTracking().Where(e => e.EpisodeId == episode.Id)
                    .OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);

                if (summary != null)
                    return summary;

                seriesRemoteId ??= episode.RemoteSeriesId;
            }
        }

        if (movieRemoteId is > 0)
        {
            var movieId = await db.Movies.AsNoTracking()
                .Where(e => e.ServerId == serverId && e.RemoteId == movieRemoteId.Value).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (movieId > 0)
                return await db.MediaHealthSummaries.AsNoTracking().Where(e => e.MovieId == movieId)
                    .OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        }

        if (seriesRemoteId is not > 0) return null;
        {
            var seriesId = await db.Series.AsNoTracking()
                .Where(e => e.ServerId == serverId && e.RemoteId == seriesRemoteId.Value).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (seriesId > 0)
                return await db.MediaHealthSummaries.AsNoTracking().Where(e => e.SeriesId == seriesId)
                    .OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    public async Task<DownloadHealthEvent?> GetMostRecentHealthEventAsync(DownloadItem item,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var downloadId = GetDownloadId(item);

        var attemptId = await db.DownloadAttempts.AsNoTracking()
            .Where(e => e.ServerId == item.ServerId && e.DownloadId == downloadId && e.Status == AttemptStatus.Active)
            .Select(e => (int?)e.Id).FirstOrDefaultAsync(cancellationToken);

        if (attemptId is null) return null;

        return await db.HealthEvents.AsNoTracking().Where(e => e.DownloadAttemptId == attemptId)
            .OrderByDescending(e => e.OccurredAt).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<MediaHealthSummary?> GetMediaSummary(DownloadItem item,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var remoteMovieId = item.MovieId;
        if (remoteMovieId > 0)
        {
            var movieId = await db.Movies.AsNoTracking()
                .Where(e => e.ServerId == item.ServerId && e.RemoteId == remoteMovieId).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (movieId <= 0) return null;

            return await db.MediaHealthSummaries.AsNoTracking().Where(e => e.MovieId == movieId)
                .OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        }

        var remoteSeriesId = item.SeriesId;
        if (remoteSeriesId > 0)
        {
            var seriesId = await db.Series.AsNoTracking()
                .Where(e => e.ServerId == item.ServerId && e.RemoteId == remoteSeriesId).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (seriesId <= 0) return null;

            return await db.MediaHealthSummaries.AsNoTracking().Where(e => e.SeriesId == seriesId)
                .OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    public async Task<List<DownloadAttempt>> GetDownloadAttemptsAsync(DownloadItem item,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        int? remoteMovieId = item.MovieId > 0 ? item.MovieId : null;
        int? remoteSeriesId = item.SeriesId > 0 ? item.SeriesId : null;
        int? remoteEpisodeId = item.EpisodeId > 0 ? item.EpisodeId : null;
        var downloadId = string.IsNullOrWhiteSpace(item.DownloadId) ? null : item.DownloadId;

        int? movieId = null;
        int? seriesId = null;
        int? episodeId = null;

        if (remoteMovieId > 0)
            movieId = await db.Movies.AsNoTracking()
                .Where(x => x.ServerId == item.ServerId && x.RemoteId == remoteMovieId).Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (remoteSeriesId > 0)
            seriesId = await db.Series.AsNoTracking()
                .Where(x => x.ServerId == item.ServerId && x.RemoteId == remoteSeriesId).Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (remoteEpisodeId > 0)
            episodeId = await db.Episodes.AsNoTracking()
                .Where(x => x.ServerId == item.ServerId && x.RemoteId == remoteEpisodeId).Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking().Where(x =>
                x.ServerId == item.ServerId && ((movieId != null && x.MovieId == movieId) ||
                                                (episodeId != null && x.EpisodeId == episodeId) ||
                                                (episodeId == null && seriesId != null && x.SeriesId == seriesId) ||
                                                (downloadId != null && x.DownloadId == downloadId)))
            .OrderByDescending(x => x.FirstSeenAt).ToListAsync(cancellationToken);
    }

    #endregion

    #region Helpers

    public static string GetDownloadId(DownloadItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.DownloadId)) return item.DownloadId.Trim();
        return $"{item.ServerId}:{item.RemoteId}:{HealthEvaluator.NormalizeTitle(item.Title)}";
    }

    public async Task<List<string>> GetMissingDownloadIdsAsync(int serverId, IEnumerable<string> activeDownloadIds,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var active = activeDownloadIds.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = await db.DownloadAttempts.AsNoTracking()
            .Where(e => e.ServerId == serverId && e.Status == AttemptStatus.Active).Select(e => e.DownloadId)
            .ToListAsync(cancellationToken);

        return candidates.Where(e => !active.Contains(e)).ToList();
    }

    #endregion
}