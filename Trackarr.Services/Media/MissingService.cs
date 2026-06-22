using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Health;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Data;
using Trackarr.Services.Settings;

namespace Trackarr.Services.Media;

public class MissingService(
    IDbContextFactory<AppDbContext> contextFactory,
    MediaServerService media,
    SettingsService settings,
    ILogger<MissingService> logger)
{
    private MissingPolicy Policy => settings.GetMissingPolicy();

    public async Task<int> EvaluateServerAsync(MediaServer server, CancellationToken ct = default)
    {
        if (!server.IsEnabled) return 0;

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var records = await db.MissingMedia.Where(r => r.ServerId == server.Id).ToListAsync(ct);
        var queue = await media.GetQueueAsync(server);
        var active = await media.GetActiveSearchesAsync(server);
        var now = DateTime.UtcNow;

        int count;

        if (server.ServiceType == ServiceType.Radarr)
        {
            var missing = await media.GetMissingMovies(server, true);
            count = missing.Count;
            await ReconcileRadarrAsync(server, db, records, missing, queue);
            await ProcessRadarrAsync(server, db, records, missing, queue, active, now);
            await ApplyRadarrFallbackAsync(server, db, records, missing);
        }
        else
        {
            var missing = await media.GetMissingEpisodes(server, true);
            count = missing.Count;
            await ReconcileSonarrAsync(server, db, records, missing, queue);
            await ProcessSonarrAsync(server, db, records, missing, queue, active, now);
            await ApplySonarrFallbackAsync(server, db, records, missing, now);
        }

        await media.UpdateServerStatsAsync(server.Id, s =>
        {
            s.LastMissingFetchAt = now;
            s.LastMissingCount = count;
        });

        await SyncMediaSummariesAsync(db, server, records, now, ct);

        await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task RunSearchFlowAsync(MediaServer server, AppDbContext db, MissingMediaRecord record, bool inQueue,
        bool isSearching, bool isAvailable, DateTime now)
    {
        record.LastEvaluatedAt = now;

        if (record.SearchState == MissingSearchState.Abandoned)
            return;

        if (!isAvailable)
        {
            record.SearchState = MissingSearchState.NotAvailable;
            record.LastSearchPushedAt = null;
            return;
        }

        if (inQueue)
        {
            record.SearchState = MissingSearchState.InQueue;
            record.LastSearchPushedAt = null;
            return;
        }

        if (record.LastSearchPushedAt is { } pushedAt)
        {
            if (now - pushedAt < Policy.SearchCooldown)
            {
                record.SearchState = MissingSearchState.SkippedCooldown;
                return;
            }

            record.FailedSearchCount++;
            record.LastSearchPushedAt = null;
            AddEvent(db, record, MissingEventType.SearchFailed);
        }

        if (record.FailedSearchCount >= Policy.AbandonAfter)
        {
            record.SearchState = MissingSearchState.Abandoned;
            AddEvent(db, record, MissingEventType.Abandoned);
            return;
        }

        if (record.LoweredToFallback && record.FailedSearchCount >= Policy.MaxFailedSearches)
        {
            record.SearchState = MissingSearchState.Abandoned;
            AddEvent(db, record, MissingEventType.Abandoned);
            return;
        }

        if (isSearching)
        {
            record.SearchState = MissingSearchState.SkippedSearching;
            return;
        }

        await PushSearchAsync(server, record);
        record.SearchState = MissingSearchState.SearchPushed;
        record.LastSearchPushedAt = now;
        AddEvent(db, record, MissingEventType.SearchPushed);
    }

    private async Task ProcessRadarrAsync(MediaServer server, AppDbContext db, List<MissingMediaRecord> records,
        List<Movie> missing, List<DownloadItem> queue, ActiveSearches active, DateTime now)
    {
        foreach (var item in missing)
        {
            var rec = records.FirstOrDefault(r => r.RemoteId == item.RemoteId && r.EpisodeId == null) ?? AddRecord(db,
                records, server.Id, item.RemoteId, null, MissingSearchTier.Series, now);

            var inQueue = queue.Any(q => q.MovieId == item.RemoteId);
            var isSearching = active.MovieIds.Contains(item.RemoteId);
            await RunSearchFlowAsync(server, db, rec, inQueue, isSearching, item.IsAvailable, now);
        }
    }

    private async Task ProcessSonarrAsync(MediaServer server, AppDbContext db, List<MissingMediaRecord> records,
        List<Episode> missing, List<DownloadItem> queue, ActiveSearches active, DateTime now)
    {
        foreach (var group in missing.GroupBy(e => e.RemoteSeriesId))
        {
            var seriesId = group.Key;
            var hasEpisodeRecords = records.Any(r => r.RemoteId == seriesId && r.EpisodeId != null);

            if (hasEpisodeRecords)
            {
                foreach (var ep in group)
                {
                    var epAvailable = ep.AirDateUtc < now;
                    var rec = records.FirstOrDefault(r => r.RemoteId == seriesId && r.EpisodeId == ep.RemoteId) ??
                              AddRecord(db, records, server.Id, seriesId, ep.RemoteId, MissingSearchTier.Episode, now);

                    var inQueue = queue.Any(q => q.EpisodeId == ep.RemoteId);
                    var isSearching = active.EpisodeIds.Contains(ep.RemoteId);
                    await RunSearchFlowAsync(server, db, rec, inQueue, isSearching, epAvailable, now);
                }

                continue;
            }

            var seriesRec = records.FirstOrDefault(r => r.RemoteId == seriesId && r.EpisodeId == null) ??
                            AddRecord(db, records, server.Id, seriesId, null, MissingSearchTier.Series, now);

            var sInQueue = queue.Any(q => q.SeriesId == seriesId);
            var sSearching = active.SeriesIds.Contains(seriesId);
            var series = await media.GetSeriesByIdAsync(server, seriesId);
            var sAvailable = series?.IsAvailable(now) ?? false;
            await RunSearchFlowAsync(server, db, seriesRec, sInQueue, sSearching, sAvailable, now);
        }
    }

    private async Task ReconcileRadarrAsync(MediaServer server, AppDbContext db, List<MissingMediaRecord> records,
        List<Movie> missing, List<DownloadItem> queue)
    {
        var missingIds = missing.Select(m => m.RemoteId).ToHashSet();

        foreach (var rec in records.Where(r => !missingIds.Contains(r.RemoteId)).ToList()
                     .Where(rec => queue.All(q => q.MovieId != rec.RemoteId)))
        {
            var movie = await media.GetMovieByIdAsync(server, rec.RemoteId);

            if (movie is null)
            {
                RemoveRecord(db, records, rec);
                continue;
            }

            if (movie.HasFile)
            {
                AddEvent(db, rec, MissingEventType.Resolved);
                RemoveRecord(db, records, rec);
            }
            else if (!movie.Monitored)
            {
                RemoveRecord(db, records, rec);
            }
        }
    }

    private async Task ReconcileSonarrAsync(MediaServer server, AppDbContext db, List<MissingMediaRecord> records,
        List<Episode> missing, List<DownloadItem> queue)
    {
        var missingEpIds = missing.Select(e => e.RemoteId).ToHashSet();
        var missingSeriesIds = missing.Select(e => e.RemoteSeriesId).ToHashSet();

        var fellOut = records.Where(r => r.EpisodeId != null
            ? !missingEpIds.Contains(r.EpisodeId.Value)
            : !missingSeriesIds.Contains(r.RemoteId)).ToList();

        if (fellOut.Count == 0) return;

        var stateBySeries = new Dictionary<int, Dictionary<int, (bool HasFile, bool Monitored)>>();

        foreach (var sid in fellOut.Select(r => r.RemoteId).Distinct())
        {
            var states = await media.GetSeriesEpisodeStatesAsync(server, sid);
            stateBySeries[sid] = states.ToDictionary(s => s.EpisodeId, s => (s.HasFile, s.Monitored));
        }

        foreach (var rec in fellOut)
        {
            var map = stateBySeries.GetValueOrDefault(rec.RemoteId);

            if (rec.EpisodeId != null)
            {
                if (queue.Any(q => q.EpisodeId == rec.EpisodeId)) continue;

                if (map is null || !map.TryGetValue(rec.EpisodeId.Value, out var st))
                {
                    RemoveRecord(db, records, rec);
                    continue;
                }

                if (st.HasFile)
                {
                    AddEvent(db, rec, MissingEventType.Resolved);
                    RemoveRecord(db, records, rec);
                }
                else if (!st.Monitored)
                {
                    RemoveRecord(db, records, rec);
                }
            }
            else
            {
                if (queue.Any(q => q.SeriesId == rec.RemoteId)) continue;
                if (map is null)
                {
                    RemoveRecord(db, records, rec);
                    continue;
                }

                var monitored = map.Values.Where(v => v.Monitored).ToList();

                if (monitored.Count > 0 && monitored.All(v => v.HasFile))
                {
                    AddEvent(db, rec, MissingEventType.Resolved);
                    RemoveRecord(db, records, rec);
                }
            }
        }
    }

    private async Task ApplyRadarrFallbackAsync(MediaServer server, AppDbContext db, List<MissingMediaRecord> records,
        List<Movie> missing)
    {
        if (server.FallBackProfileId is not { } fallbackId) return;

        var missingIds = missing.Select(m => m.RemoteId).ToHashSet();

        var targets = records.Where(r => r.EpisodeId == null && !r.LoweredToFallback &&
                                         r.FailedSearchCount >= Policy.MaxFailedSearches &&
                                         missingIds.Contains(r.RemoteId)).ToList();

        foreach (var rec in targets)
        {
            await media.SetQualityProfileAsync(server, rec.RemoteId, fallbackId);
            rec.LoweredToFallback = true;
            rec.FailedSearchCount = 0;
            rec.LastSearchPushedAt = null;
            rec.SearchState = MissingSearchState.New;
            AddEvent(db, rec, MissingEventType.DowngradedToFallback);
        }
    }

    private async Task ApplySonarrFallbackAsync(MediaServer server, AppDbContext db, List<MissingMediaRecord> records,
        List<Episode> missing, DateTime now)
    {
        var cascadeTargets = records.Where(r =>
            r.EpisodeId == null && !r.LoweredToFallback && r.SearchState != MissingSearchState.Abandoned &&
            r.FailedSearchCount >= Policy.MaxFailedSearches).ToList();

        foreach (var seriesRec in cascadeTargets)
        {
            var eps = missing.Where(e => e.RemoteSeriesId == seriesRec.RemoteId).ToList();

            foreach (var ep in eps)
            {
                if (records.Any(r => r.RemoteId == seriesRec.RemoteId && r.EpisodeId == ep.RemoteId)) continue;
                AddRecord(db, records, server.Id, seriesRec.RemoteId, ep.RemoteId, MissingSearchTier.Episode, now);
            }

            AddEvent(db, seriesRec, MissingEventType.CascadedToEpisode);
            RemoveRecord(db, records, seriesRec);
        }

        if (server.FallBackProfileId is not { } fallbackId) return;

        var missingEpIds = missing.Select(e => e.RemoteId).ToHashSet();

        var bySeries = records.Where(r => r.EpisodeId != null && missingEpIds.Contains(r.EpisodeId.Value))
            .GroupBy(r => r.RemoteId);

        foreach (var grp in bySeries)
        {
            var recs = grp.ToList();
            if (recs.Count == 0 || recs.Any(r => r.LoweredToFallback)) continue;
            if (!recs.All(r => r.FailedSearchCount >= Policy.MaxFailedSearches)) continue;

            await media.SetQualityProfileAsync(server, grp.Key, fallbackId);

            foreach (var r in recs)
            {
                r.LoweredToFallback = true;
                r.FailedSearchCount = 0;
                r.LastSearchPushedAt = null;
                r.SearchState = MissingSearchState.New;
                AddEvent(db, r, MissingEventType.DowngradedToFallback);
            }
        }
    }

    private Task PushSearchAsync(MediaServer server, MissingMediaRecord record)
    {
        if (server.ServiceType == ServiceType.Radarr)
            return media.TriggerMovieSearchAsync(server, record.RemoteId);
        if (record.Tier == MissingSearchTier.Series)
            return media.TriggerSeriesSearchAsync(server, record.RemoteId);
        return media.TriggerEpisodeSearchAsync(server, record.EpisodeId!.Value);
    }

    private async Task SyncMediaSummariesAsync(AppDbContext db, MediaServer server, List<MissingMediaRecord> records,
        DateTime now, CancellationToken ct)
    {
        if (records.Count == 0) return;

        var remoteIds = records.Select(r => r.RemoteId).Distinct().ToList();

        Dictionary<int, int> idMap;
        if (server.ServiceType == ServiceType.Radarr)
        {
            idMap = await db.Movies.Where(m => m.ServerId == server.Id && remoteIds.Contains(m.RemoteId))
                .Select(m => new { m.RemoteId, m.Id }).ToDictionaryAsync(m => m.RemoteId, m => m.Id, ct);
        }
        else
        {
            idMap = await db.Series.Where(s => s.ServerId == server.Id && remoteIds.Contains(s.RemoteId))
                .Select(s => new { s.RemoteId, s.Id }).ToDictionaryAsync(s => s.RemoteId, s => s.Id, ct);
        }

        var episodeRemoteIds = records
            .Where(r => r.EpisodeId != null).Select(r => r.EpisodeId!.Value).Distinct().ToList();

        var episodeIdMap = episodeRemoteIds.Count > 0
            ? await db.Episodes.Where(e => e.ServerId == server.Id && episodeRemoteIds.Contains(e.RemoteId))
                .Select(e => new { e.RemoteId, e.Id }).ToDictionaryAsync(e => e.RemoteId, e => e.Id, ct)
            : new Dictionary<int, int>();

        var eventCounts = await db.SearchEvents.Where(e => e.ServerId == server.Id && remoteIds.Contains(e.RemoteId))
            .GroupBy(e => new { e.RemoteId, e.EpisodeId }).Select(g => new
            {
                g.Key.RemoteId,
                g.Key.EpisodeId,
                SearchCount = g.Count(e => e.EventType == MissingEventType.SearchPushed),
                DowngradeCount = g.Count(e => e.EventType == MissingEventType.DowngradedToFallback),
                LastSearchAt = g
                    .Where(e => e.EventType == MissingEventType.SearchPushed).Max(e => (DateTime?)e.OccurredAt)
            }).ToListAsync(ct);

        foreach (var record in records)
        {
            int? localMovieId = null, localSeriesId = null, localEpisodeId = null;
            TrackedMediaType mediaType;

            if (server.ServiceType == ServiceType.Radarr)
            {
                if (!idMap.TryGetValue(record.RemoteId, out var mid)) continue;
                localMovieId = mid;
                mediaType = TrackedMediaType.Movie;
            }
            else if (record.EpisodeId != null)
            {
                if (!episodeIdMap.TryGetValue(record.EpisodeId.Value, out var eid)) continue;
                localEpisodeId = eid;
                if (idMap.TryGetValue(record.RemoteId, out var sid))
                    localSeriesId = sid;
                mediaType = TrackedMediaType.Episode;
            }
            else
            {
                if (!idMap.TryGetValue(record.RemoteId, out var sid)) continue;
                localSeriesId = sid;
                mediaType = TrackedMediaType.Series;
            }

            var summary = localMovieId > 0
                ?
                await db.MediaHealthSummaries.FirstOrDefaultAsync(s => s.MovieId == localMovieId, ct)
                : localEpisodeId > 0
                    ? await db.MediaHealthSummaries.FirstOrDefaultAsync(s => s.EpisodeId == localEpisodeId, ct)
                    : localSeriesId > 0
                        ? await db.MediaHealthSummaries.FirstOrDefaultAsync(s => s.SeriesId == localSeriesId, ct)
                        : null;

            if (summary == null)
            {
                summary = new MediaHealthSummary
                {
                    MediaType = mediaType,
                    MovieId = localMovieId,
                    SeriesId = localSeriesId,
                    EpisodeId = localEpisodeId,
                    CreatedAt = now
                };
                db.MediaHealthSummaries.Add(summary);
            }

            var counts = eventCounts.FirstOrDefault(c =>
                c.RemoteId == record.RemoteId && c.EpisodeId == record.EpisodeId);

            summary.SearchAttemptCount = counts?.SearchCount ?? 0;
            summary.QualityDowngradeCount = counts?.DowngradeCount ?? 0;

            if (record.SearchState == MissingSearchState.Abandoned && !summary.IsStuck)
            {
                summary.IsStuck = true;
                summary.StuckSince = record.LastEvaluatedAt;
                summary.StuckReasonKey = "abandoned-search";
                summary.StuckReasonMessage = "All search attempts exhausted without finding this item.";
            }
            else if (record.SearchState != MissingSearchState.Abandoned && summary.StuckReasonKey == "abandoned-search")
            {
                summary.IsStuck = false;
                summary.StuckSince = null;
                summary.StuckReasonKey = null;
                summary.StuckReasonMessage = null;
            }

            summary.UpdatedAt = now;
        }
    }

    private static MissingMediaRecord AddRecord(AppDbContext db, List<MissingMediaRecord> records, int serverId,
        int remoteId, int? episodeId, MissingSearchTier tier, DateTime now)
    {
        var rec = new MissingMediaRecord
        {
            ServerId = serverId,
            RemoteId = remoteId,
            EpisodeId = episodeId,
            Tier = tier,
            SearchState = MissingSearchState.New,
            FirstSeenMissingAt = now,
            LastEvaluatedAt = now
        };

        db.MissingMedia.Add(rec);
        records.Add(rec);
        return rec;
    }

    private static void RemoveRecord(AppDbContext db, List<MissingMediaRecord> records, MissingMediaRecord rec)
    {
        db.MissingMedia.Remove(rec);
        records.Remove(rec);
    }

    private static void AddEvent(AppDbContext db, MissingMediaRecord r, MissingEventType type)
    {
        db.SearchEvents.Add(new MissingSearchEvent
        {
            ServerId = r.ServerId,
            RemoteId = r.RemoteId,
            EpisodeId = r.EpisodeId,
            EventType = type,
            Tier = r.Tier,
            FailedSearchCount = r.FailedSearchCount
        });
    }
}
