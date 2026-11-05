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

        // Remote IDs restart at 1 on every Radarr/Sonarr instance, so they are only unique per server.
        var localMovieId = item.MovieId is > 0
            ? await db.Movies.AsNoTracking().Where(e => e.ServerId == item.ServerId && e.RemoteId == item.MovieId)
                .Select(e => (int?)e.Id).FirstOrDefaultAsync(cancellationToken)
            : null;
        var localSeriesId = item.SeriesId is > 0
            ? await db.Series.AsNoTracking().Where(e => e.ServerId == item.ServerId && e.RemoteId == item.SeriesId)
                .Select(e => (int?)e.Id).FirstOrDefaultAsync(cancellationToken)
            : null;
        var localEpisodeId = item.EpisodeId is > 0
            ? await db.Episodes.AsNoTracking().Where(e => e.ServerId == item.ServerId && e.RemoteId == item.EpisodeId)
                .Select(e => (int?)e.Id).FirstOrDefaultAsync(cancellationToken)
            : null;

        // Track Sonarr downloads per episode when the episode is known, so one bad episode doesn't put a failure
        // (and the previous-failure penalty) on every other episode of the series.
        if (mediaType == TrackedMediaType.Series && localEpisodeId is not null)
            mediaType = TrackedMediaType.Episode;

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

    public Task<MediaHealthSummary?> GetMediaSummaryAsync(AppDbContext db, DownloadAttempt attempt,
        CancellationToken cancellationToken)
    {
        return FindSummaryAsync(db, attempt.MediaType, attempt.MovieId, attempt.SeriesId, attempt.EpisodeId,
            cancellationToken);
    }

    private static async Task<MediaHealthSummary?> FindSummaryAsync(AppDbContext db, TrackedMediaType mediaType,
        int? movieId, int? seriesId, int? episodeId, CancellationToken cancellationToken)
    {
        // Check the change tracker first: the episode and series roll-up can both be created in one SaveChanges.
        var local = db.MediaHealthSummaries.Local.FirstOrDefault(e => e.MediaType == mediaType && mediaType switch
        {
            TrackedMediaType.Movie => e.MovieId == movieId,
            TrackedMediaType.Series => e.SeriesId == seriesId,
            TrackedMediaType.Episode => e.EpisodeId == episodeId,
            _ => false
        });
        if (local is not null) return local;

        return mediaType switch
        {
            TrackedMediaType.Movie when movieId.HasValue => await db.MediaHealthSummaries.FirstOrDefaultAsync(
                e => e.MediaType == TrackedMediaType.Movie && e.MovieId == movieId, cancellationToken),
            TrackedMediaType.Series when seriesId.HasValue => await db.MediaHealthSummaries.FirstOrDefaultAsync(
                e => e.MediaType == TrackedMediaType.Series && e.SeriesId == seriesId, cancellationToken),
            TrackedMediaType.Episode when episodeId.HasValue => await db.MediaHealthSummaries.FirstOrDefaultAsync(
                e => e.MediaType == TrackedMediaType.Episode && e.EpisodeId == episodeId, cancellationToken),
            _ => null
        };
    }

    public async Task UpdateMediaSummaryAsync(AppDbContext db, DownloadAttempt attempt, HealthDecision? decision,
        DateTime now, CancellationToken cancellationToken)
    {
        if (attempt.MediaType == TrackedMediaType.Unknown) return;

        await UpdateSummaryAsync(db, attempt.MediaType, attempt.MovieId, attempt.SeriesId, attempt.EpisodeId,
            decision, now, cancellationToken);

        // Episode attempts are scored against their own episode's summary, but series views still want the
        // whole picture, so the series summary is kept as a roll-up of all its attempts.
        if (attempt.MediaType == TrackedMediaType.Episode && attempt.SeriesId is not null)
        {
            await UpdateSummaryAsync(db, TrackedMediaType.Series, null, attempt.SeriesId, null, null, now,
                cancellationToken);
        }
    }

    private async Task UpdateSummaryAsync(AppDbContext db, TrackedMediaType mediaType, int? movieId, int? seriesId,
        int? episodeId, HealthDecision? decision, DateTime now, CancellationToken cancellationToken)
    {
        var summary = await FindSummaryAsync(db, mediaType, movieId, seriesId, episodeId, cancellationToken);

        if (summary is null)
        {
            summary = new MediaHealthSummary
            {
                MediaType = mediaType,
                MovieId = movieId,
                SeriesId = seriesId,
                EpisodeId = episodeId,
                CreatedAt = now
            };

            db.MediaHealthSummaries.Add(summary);
        }

        var attempts = mediaType switch
        {
            TrackedMediaType.Movie => db.DownloadAttempts.Where(e =>
                e.MediaType == TrackedMediaType.Movie && e.MovieId == movieId),
            // Any attempt for the series, whether tracked at series or episode level.
            TrackedMediaType.Series => db.DownloadAttempts.Where(e =>
                (e.MediaType == TrackedMediaType.Series || e.MediaType == TrackedMediaType.Episode) &&
                e.SeriesId == seriesId),
            TrackedMediaType.Episode => db.DownloadAttempts.Where(e =>
                e.MediaType == TrackedMediaType.Episode && e.EpisodeId == episodeId),
            _ => db.DownloadAttempts.Where(e => false)
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

        var activeAttemptIds = attempts.Where(e => e.Status == AttemptStatus.Active).Select(e => e.Id);
        var activeSnapshots = await db.HealthSnapshots.Where(e => activeAttemptIds.Contains(e.DownloadAttemptId))
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

    /// <param name="becameRemovable">True only on the scan where the attempt first became eligible for removal.</param>
    public async Task UpdateSourceSummaryAsync(AppDbContext db, DownloadAttempt attempt, HealthSnapshot snapshot,
        DownloadHealthEvent healthEvent, bool becameRemovable, DateTime now, CancellationToken cancellationToken)
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

        // Count each attempt once per source + reason, not once per scan (the new event isn't saved yet, so any
        // saved event with this reason means the attempt was already counted).
        var alreadyCounted = attempt.Id != 0 && await db.HealthEvents.AnyAsync(
            e => e.DownloadAttemptId == attempt.Id && e.ReasonKey == healthEvent.ReasonKey, cancellationToken);

        if (!alreadyCounted)
        {
            summary.AttemptCount++;
            summary.AverageScore = summary.AttemptCount == 1
                ? snapshot.VisibleScore
                : (summary.AverageScore * (summary.AttemptCount - 1) + snapshot.VisibleScore) / summary.AttemptCount;
        }

        var first = summary.AttemptCount <= 1 && !alreadyCounted;
        summary.LastSeenAt = now;
        summary.WorstScore = first ? snapshot.VisibleScore : Math.Min(summary.WorstScore, snapshot.VisibleScore);
        summary.BestScore = first ? snapshot.VisibleScore : Math.Max(summary.BestScore, snapshot.VisibleScore);

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

        // Once per attempt, when it first becomes removable, not on every scan it stays that way.
        if (becameRemovable)
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
