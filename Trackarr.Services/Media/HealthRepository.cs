using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Data;

namespace Trackarr.Services.Media;

/// <summary>
///     Persistence for the health pipeline: resolving/creating attempts and snapshots and rolling up the
///     media/source summaries. Every method takes the caller's <see cref="AppDbContext" /> so a whole
///     evaluation stays a single unit of work (one SaveChanges in <see cref="HealthService" />).
/// </summary>
public sealed class HealthRepository(HealthPolicy? policy = null)
{
    #region Dependencies

    private readonly HealthPolicy _policy = policy ?? new HealthPolicy();

    #endregion

    #region Attempt Management

    public async Task<DownloadAttempt> GetOrCreateAttemptAsync(AppDbContext db, DownloadItem item, MediaServer? server,
        DateTime now, CancellationToken cancellationToken)
    {
        var downloadId = HealthService.GetDownloadId(item);

        var attempt = await db.DownloadAttempts.Include(e => e.HealthSnapshot).FirstOrDefaultAsync(
            e => e.ServerId == item.ServerId && e.DownloadId == downloadId && e.Status == AttemptStatus.Active,
            cancellationToken);

        if (attempt is not null) return attempt;

        var mediaType = GetMediaType(item);

        var localMovieId = item.MovieId is > 0
            ? await db.Movies.AsNoTracking().Where(e => e.RemoteId == item.MovieId).Select(e => (int?)e.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var localSeriesId = item.SeriesId is > 0
            ? await db.Series.AsNoTracking().Where(e => e.RemoteId == item.SeriesId).Select(e => (int?)e.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var localEpisodeId = item.EpisodeId is > 0
            ? await db.Episodes.AsNoTracking().Where(e => e.RemoteId == item.EpisodeId).Select(e => (int?)e.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var replacementAttemptNumber = await GetReplacementAttemptNumberAsync(db, mediaType, localMovieId,
            localSeriesId, localEpisodeId, cancellationToken);

        attempt = new DownloadAttempt
        {
            ServerId = item.ServerId,
            ServiceType = server?.ServiceType ?? InferServiceType(item),
            DownloadId = downloadId,
            RemoteId = item.RemoteId,
            Title = item.Title ?? "Unknown download",
            NormalizedTitle = HealthEvaluator.NormalizeTitle(item.Title),
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

    #endregion

    #region Snapshot Management

    public async Task<HealthSnapshot> GetOrCreateSnapshotAsync(AppDbContext db, DownloadAttempt attempt, DateTime now,
        CancellationToken cancellationToken)
    {
        if (attempt.HealthSnapshot is not null) return attempt.HealthSnapshot;

        var snapshot =
            await db.HealthSnapshots.FirstOrDefaultAsync(e => e.DownloadAttemptId == attempt.Id, cancellationToken);

        if (snapshot is not null)
        {
            attempt.HealthSnapshot = snapshot;
            return snapshot;
        }

        snapshot = new HealthSnapshot
        {
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

    #endregion

    #region Summary Updates

    public async Task<MediaHealthSummary?> GetMediaSummaryAsync(AppDbContext db, DownloadAttempt attempt,
        CancellationToken cancellationToken)
    {
        return attempt.MediaType switch
        {
            TrackedMediaType.Movie when attempt.MovieId.HasValue => await db.MediaHealthSummaries.FirstOrDefaultAsync(
                e => e.MediaType == TrackedMediaType.Movie && e.MovieId == attempt.MovieId, cancellationToken),
            TrackedMediaType.Series when attempt.SeriesId.HasValue => await db.MediaHealthSummaries.FirstOrDefaultAsync(
                e => e.MediaType == TrackedMediaType.Series && e.SeriesId == attempt.SeriesId, cancellationToken),
            TrackedMediaType.Episode when attempt.EpisodeId.HasValue => await
                db.MediaHealthSummaries.FirstOrDefaultAsync(
                    e => e.MediaType == TrackedMediaType.Episode && e.EpisodeId == attempt.EpisodeId,
                    cancellationToken),
            _ => null
        };
    }

    public async Task UpdateMediaSummaryAsync(AppDbContext db, DownloadAttempt attempt, HealthDecision? decision,
        DateTime now, CancellationToken cancellationToken)
    {
        if (attempt.MediaType == TrackedMediaType.Unknown) return;

        var summary = await GetMediaSummaryAsync(db, attempt, cancellationToken);

        if (summary is null)
        {
            summary = new MediaHealthSummary
            {
                MediaType = attempt.MediaType,
                MovieId = attempt.MovieId,
                SeriesId = attempt.SeriesId,
                EpisodeId = attempt.EpisodeId,
                CreatedAt = now
            };

            db.MediaHealthSummaries.Add(summary);
        }

        var attempts = db.DownloadAttempts.Where(e => e.MediaType == attempt.MediaType);

        attempts = attempt.MediaType switch
        {
            TrackedMediaType.Movie => attempts.Where(e => e.MovieId == attempt.MovieId),
            TrackedMediaType.Series => attempts.Where(e => e.SeriesId == attempt.SeriesId),
            TrackedMediaType.Episode => attempts.Where(e => e.EpisodeId == attempt.EpisodeId),
            _ => attempts.Where(e => false)
        };

        summary.AttemptCount = await attempts.CountAsync(cancellationToken);
        summary.ActiveAttemptCount =
            await attempts.CountAsync(e => e.Status == AttemptStatus.Active, cancellationToken);
        summary.SuccessCount = await attempts.CountAsync(e => e.WasSuccessful || e.Status == AttemptStatus.Imported,
            cancellationToken);
        summary.FailureCount =
            await attempts.CountAsync(e => e.Status == AttemptStatus.Failed || e.Status == AttemptStatus.Disappeared,
                cancellationToken);
        summary.RemovalCount =
            await attempts.CountAsync(e => e.Status == AttemptStatus.RemovedByTrackarr, cancellationToken);
        summary.ReplacementCount =
            await attempts.CountAsync(e => e.Status == AttemptStatus.Replaced, cancellationToken);

        summary.LastAttemptAt = await attempts.MaxAsync(e => (DateTime?)e.FirstSeenAt, cancellationToken);
        summary.LastSuccessAt = await attempts.Where(e => e.WasSuccessful || e.Status == AttemptStatus.Imported)
            .MaxAsync(e => e.CompletedAt, cancellationToken);
        summary.LastFailureAt = await attempts
            .Where(e => e.Status == AttemptStatus.Failed || e.Status == AttemptStatus.Disappeared)
            .MaxAsync(e => e.FailedAt, cancellationToken);
        summary.LastRemovalAt = await attempts.Where(e => e.Status == AttemptStatus.RemovedByTrackarr)
            .MaxAsync(e => e.RemovedAt, cancellationToken);
        summary.LastReplacementAt = await attempts.Where(e => e.Status == AttemptStatus.Replaced)
            .MaxAsync(e => e.ReplacedAt, cancellationToken);

        var activeSnapshots = await db.HealthSnapshots.Where(e => e.DownloadAttempt != null)
            .Where(e => e.DownloadAttempt!.MediaType == attempt.MediaType)
            .Where(e => e.DownloadAttempt!.Status == AttemptStatus.Active).Where(e =>
                (attempt.MediaType == TrackedMediaType.Movie && e.DownloadAttempt!.MovieId == attempt.MovieId) ||
                (attempt.MediaType == TrackedMediaType.Series && e.DownloadAttempt!.SeriesId == attempt.SeriesId) ||
                (attempt.MediaType == TrackedMediaType.Episode && e.DownloadAttempt!.EpisodeId == attempt.EpisodeId))
            .ToListAsync(cancellationToken);

        summary.CurrentBestScore = activeSnapshots.Count == 0 ? 0 : activeSnapshots.Max(e => e.VisibleScore);
        summary.CurrentWorstScore = activeSnapshots.Count == 0 ? 0 : activeSnapshots.Min(e => e.VisibleScore);
        summary.CurrentState = activeSnapshots.Count == 0
            ? HealthState.Unknown
            : activeSnapshots.OrderBy(e => e.VisibleScore).First().State;

        var isStuckFromDownloads =
            summary.FailureCount >= 3 || summary.RemovalCount >= 3 || decision?.ShouldRemove == true;

        if (isStuckFromDownloads)
        {
            summary.IsStuck = true;
            summary.StuckSince ??= now;
            summary.StuckReasonKey = "repeated-download-failure";
            summary.StuckReasonMessage = "Media has repeated failed or removed download attempts.";
        }
        else if (summary.StuckReasonKey != "abandoned-search")
        {
            summary.IsStuck = false;
            summary.StuckSince = null;
            summary.StuckReasonKey = null;
            summary.StuckReasonMessage = null;
        }

        summary.UpdatedAt = now;
    }

    public async Task UpdateSourceSummaryAsync(AppDbContext db, DownloadAttempt attempt, HealthSnapshot snapshot,
        DownloadHealthEvent healthEvent, DateTime now, CancellationToken cancellationToken)
    {
        var summary = await db.SourceHealthSummaries.FirstOrDefaultAsync(
            e => e.ServerId == attempt.ServerId && e.ServiceType == attempt.ServiceType &&
                 e.Indexer == attempt.Indexer && e.DownloadClient == attempt.DownloadClient &&
                 e.Protocol == attempt.Protocol && e.ReleaseKey == attempt.ReleaseKey &&
                 e.ReasonKey == healthEvent.ReasonKey, cancellationToken);

        if (summary is null)
        {
            summary = new SourceHealthSummary
            {
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
        summary.AverageScore = summary.AttemptCount == 1
            ? snapshot.VisibleScore
            : (summary.AverageScore * (summary.AttemptCount - 1) + snapshot.VisibleScore) / summary.AttemptCount;

        summary.WorstScore = summary.AttemptCount == 1
            ? snapshot.VisibleScore
            : Math.Min(summary.WorstScore, snapshot.VisibleScore);
        summary.BestScore = summary.AttemptCount == 1
            ? snapshot.VisibleScore
            : Math.Max(summary.BestScore, snapshot.VisibleScore);

        if (healthEvent.IsBadObservation)
        {
            summary.FailureCount++;
            summary.LastFailureAt = now;
            summary.ReputationScore = Math.Max(0, summary.ReputationScore - 3);
        }

        if (healthEvent.IsGoodObservation)
        {
            summary.SuccessCount++;
            summary.LastSuccessAt = now;
            summary.ReputationScore = Math.Min(100, summary.ReputationScore + 1);
        }

        if (healthEvent.IsMeaningfulRecovery)
        {
            summary.RecoveryCount++;
            summary.ReputationScore = Math.Min(100, summary.ReputationScore + 2);
        }

        if (snapshot.State == HealthState.Remove)
        {
            summary.RemovalCount++;
            summary.ReputationScore = Math.Max(0, summary.ReputationScore - 5);
        }

        summary.UpdatedAt = now;
    }

    #endregion

    #region Helpers

    private static async Task<int> GetReplacementAttemptNumberAsync(AppDbContext db, TrackedMediaType mediaType,
        int? movieId, int? seriesId, int? episodeId, CancellationToken cancellationToken)
    {
        var query = db.DownloadAttempts.Where(e => e.MediaType == mediaType);

        query = mediaType switch
        {
            TrackedMediaType.Movie => query.Where(e => e.MovieId == movieId),
            TrackedMediaType.Series => query.Where(e => e.SeriesId == seriesId),
            TrackedMediaType.Episode => query.Where(e => e.EpisodeId == episodeId),
            _ => query.Where(e => false)
        };

        return await query.CountAsync(cancellationToken) + 1;
    }

    private static TrackedMediaType GetMediaType(DownloadItem item)
    {
        if (item.MovieId > 0) return TrackedMediaType.Movie;

        if (item.SeriesId > 0 || item.EpisodeId > 0) return TrackedMediaType.Series;

        return TrackedMediaType.Unknown;
    }

    private static ServiceType InferServiceType(DownloadItem item)
    {
        if (item.EpisodeId > 0 || item.SeriesId > 0) return ServiceType.Sonarr;

        return ServiceType.Radarr;
    }

    private static string BuildReleaseKey(DownloadItem item)
    {
        var title = HealthEvaluator.NormalizeTitle(item.Title);
        var indexer = HealthEvaluator.NormalizeTitle(item.Indexer);
        var protocol = HealthEvaluator.NormalizeTitle(item.Protocol);

        return string.Join(":", new[] { title, indexer, protocol }.Where(e => !string.IsNullOrWhiteSpace(e)));
    }

    #endregion
}
