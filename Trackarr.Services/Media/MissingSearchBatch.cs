using Trackarr.Core.Media.Missing;

namespace Trackarr.Services.Media;

/// <summary>
///     Searches collected during one missing-media run for one server, sent at the end as as few commands as
///     possible (one MoviesSearch, one EpisodeSearch, one SeriesSearch per series). Holds at most
///     <c>capacity</c> items so a large library can't flood the indexers in a single run.
/// </summary>
public sealed class MissingSearchBatch(int capacity)
{
    private readonly List<int> _movieIds = [];
    private readonly List<int> _seriesIds = [];
    private readonly List<int> _episodeIds = [];

    public IReadOnlyList<int> MovieIds => _movieIds;
    public IReadOnlyList<int> SeriesIds => _seriesIds;
    public IReadOnlyList<int> EpisodeIds => _episodeIds;

    public int Count => _movieIds.Count + _seriesIds.Count + _episodeIds.Count;
    public bool IsFull => Count >= capacity;

    /// <summary>Adds the record's search if there's room. Returns false when the batch is full.</summary>
    public bool TryAdd(bool isRadarr, MissingMediaRecord record)
    {
        if (IsFull) return false;

        if (isRadarr)
            Add(_movieIds, record.RemoteId);
        else if (record.Tier == MissingSearchTier.Series || record.EpisodeId is null)
            Add(_seriesIds, record.RemoteId);
        else
            Add(_episodeIds, record.EpisodeId.Value);

        return true;
    }

    private static void Add(List<int> ids, int id)
    {
        if (!ids.Contains(id)) ids.Add(id);
    }
}
