using Trackarr.Core.Media.Shows;

namespace Trackarr.Core.Media.Missing;

public static class MissingEpisodeCounts
{
    public static int GetTotalEpisodeCount(IEnumerable<Episode> episodes, int? serverId, int remoteSeriesId)
    {
        return episodes.Count(e => e.ServerId == serverId && e.RemoteSeriesId == remoteSeriesId);
    }

    // Before a series cascades into one MissingMediaRecord per episode, it is tracked as a
    // single record, so recordCount (the tracking-record count) is always 1 even when several
    // episodes are actually missing. Derive the real count from episode state instead, falling
    // back to recordCount once cascaded (where the record count is already accurate per-episode)
    // or when episode state hasn't been synced yet.
    public static int GetMissingEpisodeCount(IEnumerable<Episode> episodes, int? serverId, int remoteSeriesId,
        bool isEpisodeGroup, int recordCount, DateTime now)
    {
        if (isEpisodeGroup) return recordCount;

        var actual = episodes.Count(e =>
            e.ServerId == serverId && e.RemoteSeriesId == remoteSeriesId &&
            e.Monitored && !e.HasFile && e.AirDateUtc < now);

        return actual > 0 ? actual : recordCount;
    }
}
