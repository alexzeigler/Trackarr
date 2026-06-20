using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Data;

namespace Trackarr.Services.Media;

public class MissingService(
    IDbContextFactory<AppDbContext> contextFactory,
    MediaServerService media,
    ILogger<MissingService> logger)
{
    private readonly MissingPolicy _policy = new();

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

        await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task RunSearchFlowAsync(
        MediaServer server, AppDbContext db, MissingMediaRecord record,
        bool inQueue, bool isSearching, DateTime now)
    {
        record.LastEvaluatedAt = now;

        if (inQueue)
        {
            record.SearchState = MissingSearchState.Idle;
            return;
        }

        if (record.SearchState == MissingSearchState.PendingVerification)
        {
            if (now - (record.LastSearchPushedAt ?? now) < _policy.SearchCooldown) return;
            record.FailedSearchCount++;
            record.SearchState = MissingSearchState.Idle;
            AddEvent(db, record, MissingEventType.SearchFailed);
        }

        if (isSearching) return;
        if (record.LastSearchPushedAt is { } last && now - last < _policy.SearchCooldown) return;

        await PushSearchAsync(server, record);
        record.SearchState = MissingSearchState.PendingVerification;
        record.LastSearchPushedAt = now;
        AddEvent(db, record, MissingEventType.SearchPushed);
    }

    private async Task ProcessRadarrAsync(
        MediaServer server, AppDbContext db, List<MissingMediaRecord> records,
        List<Movie> missing, List<DownloadItem> queue, ActiveSearches active, DateTime now)
    {
        foreach (var item in missing)
        {
            var rec = records.FirstOrDefault(r => r.RemoteId == item.RemoteId && r.EpisodeId == null)
                      ?? AddRecord(db, records, server.Id, item.RemoteId, null, MissingSearchTier.Series, now);

            var inQueue = queue.Any(q => q.MovieId == item.RemoteId);
            var isSearching = active.MovieIds.Contains(item.RemoteId);
            await RunSearchFlowAsync(server, db, rec, inQueue, isSearching, now);
        }
    }

    private async Task ProcessSonarrAsync(
        MediaServer server, AppDbContext db, List<MissingMediaRecord> records,
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
                    var rec = records.FirstOrDefault(r => r.RemoteId == seriesId && r.EpisodeId == ep.RemoteId)
                              ?? AddRecord(db, records, server.Id, seriesId, ep.RemoteId, MissingSearchTier.Episode,
                                  now);

                    var inQueue = queue.Any(q => q.EpisodeId == ep.RemoteId);
                    var isSearching = active.EpisodeIds.Contains(ep.RemoteId);
                    await RunSearchFlowAsync(server, db, rec, inQueue, isSearching, now);
                }

                continue;
            }

            var seriesRec = records.FirstOrDefault(r => r.RemoteId == seriesId && r.EpisodeId == null)
                            ?? AddRecord(db, records, server.Id, seriesId, null, MissingSearchTier.Series, now);

            var sInQueue = queue.Any(q => q.SeriesId == seriesId);
            var sSearching = active.SeriesIds.Contains(seriesId);
            await RunSearchFlowAsync(server, db, seriesRec, sInQueue, sSearching, now);
        }
    }

    private async Task ReconcileRadarrAsync(
        MediaServer server, AppDbContext db, List<MissingMediaRecord> records,
        List<Movie> missing, List<DownloadItem> queue)
    {
        var missingIds = missing.Select(m => m.RemoteId).ToHashSet();

        foreach (var rec in records.Where(r => !missingIds.Contains(r.RemoteId)).ToList())
        {
            if (queue.Any(q => q.MovieId == rec.RemoteId)) continue;

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

    private async Task ReconcileSonarrAsync(
        MediaServer server, AppDbContext db, List<MissingMediaRecord> records,
        List<Episode> missing, List<DownloadItem> queue)
    {
        var missingEpIds = missing.Select(e => e.RemoteId).ToHashSet();
        var missingSeriesIds = missing.Select(e => e.RemoteSeriesId).ToHashSet();

        var fellOut = records.Where(r => r.EpisodeId != null
                ? !missingEpIds.Contains(r.EpisodeId.Value)
                : !missingSeriesIds.Contains(r.RemoteId))
            .ToList();

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

    private async Task ApplyRadarrFallbackAsync(
        MediaServer server, AppDbContext db, List<MissingMediaRecord> records, List<Movie> missing)
    {
        if (server.FallBackProfileId is not { } fallbackId) return;

        var missingIds = missing.Select(m => m.RemoteId).ToHashSet();

        var targets = records.Where(r => r.EpisodeId == null
                                         && !r.LoweredToFallback
                                         && r.FailedSearchCount >= _policy.MaxFailedSearches
                                         && missingIds.Contains(r.RemoteId)).ToList();

        foreach (var rec in targets)
        {
            await media.SetQualityProfileAsync(server, rec.RemoteId, fallbackId);
            rec.LoweredToFallback = true;
            AddEvent(db, rec, MissingEventType.DowngradedToFallback);
        }
    }

    private async Task ApplySonarrFallbackAsync(
        MediaServer server, AppDbContext db, List<MissingMediaRecord> records,
        List<Episode> missing, DateTime now)
    {
        var cascadeTargets = records.Where(r => r.EpisodeId == null
                                                && !r.LoweredToFallback
                                                && r.FailedSearchCount >= _policy.MaxFailedSearches).ToList();

        foreach (var seriesRec in cascadeTargets)
        {
            foreach (var ep in missing.Where(e => e.RemoteSeriesId == seriesRec.RemoteId))
            {
                if (records.Any(r => r.RemoteId == seriesRec.RemoteId && r.EpisodeId == ep.RemoteId)) continue;
                AddRecord(db, records, server.Id, seriesRec.RemoteId, ep.RemoteId, MissingSearchTier.Episode, now);
            }

            AddEvent(db, seriesRec, MissingEventType.CascadedToEpisode);
            RemoveRecord(db, records, seriesRec);
        }

        if (server.FallBackProfileId is not { } fallbackId) return;

        var missingEpIds = missing.Select(e => e.RemoteId).ToHashSet();

        var bySeries = records
            .Where(r => r.EpisodeId != null && missingEpIds.Contains(r.EpisodeId.Value))
            .GroupBy(r => r.RemoteId);

        foreach (var grp in bySeries)
        {
            var recs = grp.ToList();
            if (recs.Count == 0 || recs.Any(r => r.LoweredToFallback)) continue;
            if (!recs.All(r => r.FailedSearchCount >= _policy.MaxFailedSearches)) continue;

            await media.SetQualityProfileAsync(server, grp.Key, fallbackId);

            foreach (var r in recs)
            {
                r.LoweredToFallback = true;
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

    private static MissingMediaRecord AddRecord(
        AppDbContext db, List<MissingMediaRecord> records,
        int serverId, int remoteId, int? episodeId, MissingSearchTier tier, DateTime now)
    {
        var rec = new MissingMediaRecord
        {
            ServerId = serverId,
            RemoteId = remoteId,
            EpisodeId = episodeId,
            Tier = tier,
            SearchState = MissingSearchState.Idle,
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